using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using AspNetCore.SignalR.Npgsql.Internal;
using AspNetCore.SignalR.Npgsql.Messages;
using System.Text.Json;
using System.Threading.Channels;

namespace AspNetCore.SignalR.Npgsql;

public class PostgresHubLifetimeManager<THub> : HubLifetimeManager<THub>, IDisposable
    where THub : Hub
{
    private readonly ILogger<PostgresHubLifetimeManager<THub>> _logger;
    private readonly IOptions<NpgsqlOption> _options;
    private readonly PostgresProtocol _protocol;
    
    private readonly string _serverName = GenerateServerName();


    private readonly Channel<Notification> _notificationChannel;

    private readonly HubConnectionStore _connections = new();
    private readonly SubscriptionManager _groups = new SubscriptionManager();
    private readonly SubscriptionManager _users = new SubscriptionManager();
    
    private readonly AckHandler _ackHandler;
    private int _internalId;
    
    private volatile bool _isInitialized = false;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly NpgsqlDataSource _dataSource;

    public PostgresHubLifetimeManager(
        ILogger<PostgresHubLifetimeManager<THub>> logger, 
        IOptions<NpgsqlOption> options, 
        IHostApplicationLifetime lifetime,
        IHubProtocolResolver hubProtocolResolver,
        IOptions<HubOptions>? globalHubOptions,
        IOptions<HubOptions<THub>>? hubOptions)
    {
        _logger = logger;
        _options = options;
        _ackHandler = new AckHandler();
        
        // Validate required options
        if (string.IsNullOrEmpty(_options.Value.ConnectionString))
            throw new InvalidOperationException("ConnectionString is required");
            
        _dataSource = NpgsqlDataSource.Create(_options.Value.ConnectionString);
        
        if (globalHubOptions != null && hubOptions != null)
        {
            _protocol = new PostgresProtocol(new DefaultHubMessageSerializer(hubProtocolResolver, globalHubOptions.Value.SupportedProtocols, hubOptions.Value.SupportedProtocols));
        }
        else
        {
            var supportedProtocols = hubProtocolResolver.AllProtocols.Select(p => p.Name).ToList();
            _protocol = new PostgresProtocol(new DefaultHubMessageSerializer(hubProtocolResolver, supportedProtocols, null));
        }
        _notificationChannel = Channel.CreateUnbounded<Notification>();
        lifetime.ApplicationStarted.Register(() => _ = EnsurePostgresInitializedAsync());
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync(HubConnectionContext connection)
    {
        try
        {
            await EnsurePostgresInitializedAsync();
            var feature = new SqlFeature();
            connection.Features.Set<ISqlFeature>(feature);

            var userTask = Task.CompletedTask;

            _connections.Add(connection);

            if (!string.IsNullOrEmpty(connection.UserIdentifier))
            {
                userTask = _users.AddSubscriptionAsync(connection.UserIdentifier, connection);
            }

            await userTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during connection setup for connection {ConnectionId}", connection.ConnectionId);
            throw;
        }
    }
    
    /// <inheritdoc />
    public override Task OnDisconnectedAsync(HubConnectionContext connection)
    {
        _connections.Remove(connection);

        var tasks = new List<Task>();

        var feature = connection.Features.Get<ISqlFeature>()!;

        var groupNames = feature.Groups;

        // Copy the groups to an array here because they get removed from this collection
        // in RemoveFromGroupAsync
        foreach (var group in groupNames)
        {
            // Use RemoveGroupAsyncCore because the connection is local, and we don't want to
            // accidentally go to other servers with our remove request.
            tasks.Add(RemoveGroupAsyncCore(connection, group));
        }

        if (!string.IsNullOrEmpty(connection.UserIdentifier))
        {
            tasks.Add(_users.RemoveSubscriptionAsync(connection.UserIdentifier!, connection));
        }

        return Task.WhenAll(tasks);
    }

    
    

    /// <inheritdoc />
    public override Task SendAllAsync(string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        var message = _protocol.WriteInvocationAll(methodName, args, null);
        return PublishAsync(MessageType.InvocationAll, message);
    }
    
    /// <inheritdoc />
    public override Task SendAllExceptAsync(string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = new CancellationToken())
    {
        var message = _protocol.WriteInvocationAll(methodName, args, excludedConnectionIds);
        return PublishAsync(MessageType.InvocationAll, message);
    }
    
    /// <inheritdoc />
    public override Task SendConnectionAsync(string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        if (connectionId == null)
        {
            throw new ArgumentNullException(nameof(connectionId));
        }

        // If the connection is local we can skip sending the message through the bus since we require sticky connections.
        // This also saves serializing and deserializing the message!
        var connection = _connections[connectionId];
        if (connection != null)
        {
            return connection.WriteAsync(new InvocationMessage(methodName, args), cancellationToken).AsTask();
        }

        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationConnection, connectionId, methodName, args, null);
        return PublishAsync(MessageType.InvocationConnection, message);
    }
    public override Task SendConnectionsAsync(IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        ArgumentNullException.ThrowIfNull(connectionIds);

        var publishTasks = new List<Task>(connectionIds.Count);
        foreach (var connectionId in connectionIds)
        {
            publishTasks.Add(SendConnectionAsync(connectionId, methodName, args, cancellationToken));
        }

        return Task.WhenAll(publishTasks);
    }
    public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        if (groupName == null)
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationGroup, groupName, methodName, args, null);
        return PublishAsync(MessageType.InvocationGroup, message);
    }
    public override Task SendGroupsAsync(IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        ArgumentNullException.ThrowIfNull(groupNames);
        var publishTasks = new List<Task>(groupNames.Count);

        foreach (var groupName in groupNames)
        {
            if (!string.IsNullOrEmpty(groupName))
            {
                publishTasks.Add(SendGroupAsync(groupName, methodName, args, cancellationToken));
            }
        }

        return Task.WhenAll(publishTasks);
    }
    public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = new CancellationToken())
    {
        ArgumentNullException.ThrowIfNull(groupName);

        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationGroup, groupName, methodName, args, excludedConnectionIds);
        return PublishAsync(MessageType.InvocationGroup, message);
    }
    public override Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationUser, userId, methodName, args, null);
        return PublishAsync(MessageType.InvocationUser, message);
    }
    public override Task SendUsersAsync(IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        if (userIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        var publishTasks = new List<Task>(userIds.Count);
        foreach (var userId in userIds)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                publishTasks.Add(SendUserAsync(userId, methodName, args, cancellationToken));
            }
        }

        return Task.WhenAll(publishTasks);
    }
    public override Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = new CancellationToken())
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(groupName);

        var connection = _connections[connectionId];
        if (connection != null)
        {
            // short circuit if connection is on this server
            return AddGroupAsyncCore(connection, groupName);
        }

        return SendGroupActionAndWaitForAck(connectionId, groupName, GroupAction.Add);
    }
    public override Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = new CancellationToken())
    {
        ArgumentNullException.ThrowIfNull(connectionId);
        ArgumentNullException.ThrowIfNull(groupName);

        var connection = _connections[connectionId];
        if (connection != null)
        {
            // short circuit if connection is on this server
            return RemoveGroupAsyncCore(connection, groupName);
        }

        return SendGroupActionAndWaitForAck(connectionId, groupName, GroupAction.Remove);
    }
    
    public void Dispose()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }
        
        _cancellationTokenSource.Dispose();
        _initSemaphore.Dispose();
        _dataSource.Dispose();
    }
    
    private async Task SendGroupActionAndWaitForAck(string connectionId, string groupName, GroupAction action)
    {
        var id = Interlocked.Increment(ref _internalId);
        var ack = _ackHandler.CreateAck(id);
        // Send Add/Remove Group to other servers and wait for an ack or timeout
        var message = _protocol.WriteGroupCommand(new SqlServerGroupCommand(id, _serverName, action, groupName, connectionId));
        await PublishAsync(MessageType.Group, message);

        await ack;
    }
    
    private Task AddGroupAsyncCore(HubConnectionContext connection, string groupName)
    {
        var feature = connection.Features.Get<ISqlFeature>()!;
        var groupNames = feature.Groups;

        lock (groupNames)
        {
            // Connection already in group
            if (!groupNames.Add(groupName))
            {
                return Task.CompletedTask;
            }
        }

        return _groups.AddSubscriptionAsync(groupName, connection);
    }
    
    private async Task RemoveGroupAsyncCore(HubConnectionContext connection, string groupName)
    {
        await _groups.RemoveSubscriptionAsync(groupName, connection);

        var feature = connection.Features.Get<ISqlFeature>()!;
        var groupNames = feature.Groups;
        lock (groupNames)
        {
            groupNames.Remove(groupName);
        }
    }
    
    
    private async Task EnsurePostgresInitializedAsync()
    {
        if (_isInitialized) return;
        
        await _initSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            if (_isInitialized) return;

            var notificationChannel = _options.Value.NotificationChannel;
            // Ensure schema exists and create message table in a single command
            var schema = _options.Value.SchemaName;
            var tableName = $"{_options.Value.TableSlugGenerator(typeof(THub))}_Messages";
            var createSchemaAndTableSql = $@"
                CREATE SCHEMA IF NOT EXISTS ""{schema}"";
                CREATE TABLE IF NOT EXISTS ""{schema}"".""{tableName}"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Payload"" BYTEA NOT NULL,
                    ""InsertedOn"" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                
                -- Drop existing trigger and function if they exist
                DROP TRIGGER IF EXISTS ""{tableName}""_insert_trigger ON ""{schema}"".""{tableName}"";
                DROP FUNCTION IF EXISTS notify_""{tableName}""_change();
                
                -- Create a function to notify on new message inserts
                CREATE OR REPLACE FUNCTION notify_""{tableName}""_change()
                RETURNS TRIGGER AS $$
                BEGIN
                    PERFORM pg_notify('{notificationChannel}', json_build_object(
                        'Id', NEW.""Id"",
                        'Payload', encode(NEW.""Payload"", 'base64'),
                        'InsertedOn', NEW.""InsertedOn""
                    )::text);
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                -- Create a trigger that calls the function after each insert
                CREATE TRIGGER ""{tableName}""_insert_trigger
                AFTER INSERT ON ""{schema}"".""{tableName}""
                FOR EACH ROW
                EXECUTE FUNCTION notify_""{tableName}""_change();
                ";
            
            await using var cmd = _dataSource.CreateCommand(createSchemaAndTableSql);
            await cmd.ExecuteNonQueryAsync(_cancellationTokenSource.Token);

            // Start background tasks
            _ = Task.Run(() => ListenForNotifications(_notificationChannel.Writer, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _ = Task.Run(() => ProcessNotifications(_notificationChannel.Reader, _cancellationTokenSource.Token), _cancellationTokenSource.Token);

            _isInitialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task ListenForNotifications(ChannelWriter<Notification> writer, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_options.Value.ConnectionString);
            conn.Notification += (_, args) =>
            {
                Console.WriteLine($"[Listener] Received notification on channel '{args.Channel}' from PID {args.PID}. Payload: {args.Payload}");
                if (args.Channel == _options.Value.NotificationChannel)
                {
                    try
                    {
                        // Parse the JSON payload that contains base64 encoded binary data
                        using var doc = JsonDocument.Parse(args.Payload);
                        var root = doc.RootElement;
                        
                        var id = root.GetProperty("Id").GetInt32();
                        var payloadBase64 = root.GetProperty("Payload").GetString();
                        var insertedOn = root.GetProperty("InsertedOn").GetDateTime();
                        
                        if (payloadBase64 != null)
                        {
                            var payloadBytes = Convert.FromBase64String(payloadBase64);
                            var message = new Message(id, payloadBytes, insertedOn);
                            writer.TryWrite(new Notification(message, args.Channel, args.PID));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Listener Error] Failed to deserialize payload: {args.Payload}. Error: {ex.Message}");
                    }
                }
            };

            await conn.OpenAsync(cancellationToken);
            Console.WriteLine("[Listener] Connection opened. Sending LISTEN command...");

            // Send the LISTEN command
            await using (var cmd = new NpgsqlCommand($"LISTEN {_options.Value.NotificationChannel}", conn))
            {
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            Console.WriteLine($"[Listener] Successfully listening on channel '{_options.Value.NotificationChannel}'. Keeping connection alive...");

            // Keep the connection open until cancellation is requested
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Listener] Notification listener cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Listener Error] Unexpected error in notification listener: {ex.Message}");
        }
    }


    private async Task ProcessNotifications(ChannelReader<Notification> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var notification in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    Console.WriteLine($"[Processor] Processing incoming message: Id={notification.Message.Id}");
                    var payload = new ReadOnlyMemory<byte>(notification.Message.Payload);
                    var messageType = _protocol.ReadMessageType(payload); 
                    var result = messageType switch
                    {
                        MessageType.Ack => HandleAck(payload),
                        MessageType.Group => HandleGroup(payload),
                        MessageType.InvocationAll => HandleInvocationAll(payload),
                        MessageType.InvocationGroup => HandleInvocationGroup(payload),
                        MessageType.InvocationConnection => HandleInvocationConnection(payload),
                        MessageType.InvocationUser => HandleInvocationUser(payload),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    await result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing notification with Id={NotificationId}", notification.Message.Id);
                    // Continue processing other notifications
                }
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("[Processor] Notification processor cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in notification processor");
        }
    }
    private async Task HandleInvocationUser(ReadOnlyMemory<byte> payload)
    {
        var multiInvocation = _protocol.ReadTargetedInvocation(payload);
        var connections = _users.Get(multiInvocation.Target);
        var invocation = multiInvocation.Invocation;
        await ExecuteInvocation(invocation, connections);
    }
    private async Task HandleInvocationConnection(ReadOnlyMemory<byte> payload)
    {
        var connectionInvocation = _protocol.ReadTargetedInvocation(payload);
        var userConnection = _connections[connectionInvocation.Target];
        if (userConnection == null) return; 
        await userConnection.WriteAsync(connectionInvocation.Invocation.Message);
    }
    private async Task HandleInvocationGroup(ReadOnlyMemory<byte> payload)
    {
        var multiInvocation = _protocol.ReadTargetedInvocation(payload);
        var connections = _groups.Get(multiInvocation.Target);
        var invocation = multiInvocation.Invocation;
        await ExecuteInvocation(invocation, connections);
    }
    private async Task HandleInvocationAll(ReadOnlyMemory<byte> payload)
    {
        var invocation = _protocol.ReadInvocationAll(payload);
        await ExecuteInvocation(invocation, _connections);
    }

    
    private async Task HandleGroup(ReadOnlyMemory<byte> payload)
    {
        var groupMessage = _protocol.ReadGroupCommand(payload);

        var userConnection = _connections[groupMessage.ConnectionId];
        if (userConnection == null)
        {
            // user not on this server
            return;
        }

        switch (groupMessage.Action)
        {
            case GroupAction.Remove:
                await RemoveGroupAsyncCore(userConnection, groupMessage.GroupName);
                break;
            case GroupAction.Add:
                await AddGroupAsyncCore(userConnection, groupMessage.GroupName);
                break;
        }

        // Send an ack to the server that sent the original command.
        await PublishAsync(MessageType.Ack, _protocol.WriteAck(groupMessage.Id, groupMessage.ServerName));
    }
    private Task HandleAck(ReadOnlyMemory<byte> payload)
    {
        var ack = _protocol.ReadAck(payload);
        if (ack.ServerName != _serverName) return Task.CompletedTask;
        _ackHandler.TriggerAck(ack.Id);
        return Task.CompletedTask;
    }

    
    private static async Task ExecuteInvocation(SqlServerInvocation invocation, HubConnectionStore? connections)
    {
        if(connections == null) return;
        var tasks = new List<Task>(connections.Count);
        foreach (var connection in connections)
        {
            if (invocation.ExcludedConnectionIds?.Contains(connection.ConnectionId) == true)
            {
                continue;
            }
            tasks.Add(connection.WriteAsync(invocation.Message).AsTask());
        }
        await Task.WhenAll(tasks);
    }

    private static string GenerateServerName()
    {
        // Use the machine name for convenient diagnostics, but add a guid to make it unique.
        // Example: MyServerName_02db60e5fab243b890a847fa5c4dcb29
        return $"{Environment.MachineName}_{Guid.NewGuid():N}";
    }

    private interface ISqlFeature
    {
        HashSet<string> Groups { get; }
    }

    private class SqlFeature : ISqlFeature
    {
        public HashSet<string> Groups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
    
    public record Message(int Id, byte[] Payload, DateTime InsertedOn);
    
    public record Notification(Message Message, string Channel, int PID);
    
    private async Task PublishAsync(MessageType type, byte[] payload)
    {
        try
        {
            // Only ensure initialization on first call, prevent recursion
            if (!_isInitialized)
            {
                await EnsurePostgresInitializedAsync();
            }
            
            _logger.LogInformation("Published message of type {MessageType}", type);

            // Use the shared data source for connection pooling
            var schema = _options.Value.SchemaName;
            var tableName = $"{_options.Value.TableSlugGenerator(typeof(THub))}_Messages";
            
            var insertSql = $@"INSERT INTO ""{schema}"".""{tableName}"" (""Payload"") VALUES (@payload)";
            
            await using var cmd = _dataSource.CreateCommand(insertSql);
            cmd.Parameters.AddWithValue("@payload", payload);
            await cmd.ExecuteNonQueryAsync(_cancellationTokenSource.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message of type {MessageType}", type);
            throw;
        }
    }
}
