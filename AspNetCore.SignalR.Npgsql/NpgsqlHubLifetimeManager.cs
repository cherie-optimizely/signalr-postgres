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

/// <summary>
/// A PostgreSQL-based implementation of <see cref="HubLifetimeManager{THub}"/> that manages SignalR hub connections
/// and enables communication between multiple server instances using PostgreSQL as a backplane.
/// This implementation uses PostgreSQL's LISTEN/NOTIFY mechanism for real-time message distribution.
/// </summary>
/// <typeparam name="THub">The type of hub being managed.</typeparam>
public class NpgsqlHubLifetimeManager<THub> : HubLifetimeManager<THub>, IDisposable
    where THub : Hub
{
    private readonly ILogger<NpgsqlHubLifetimeManager<THub>> _logger;
    private readonly IOptions<NpgsqlOption> _options;
    private readonly PostgresProtocol _protocol;

    private readonly string _serverName = GenerateServerName();


    private readonly Channel<Notification> _notificationChannel;

    private readonly HubConnectionStore _connections = new();
    private readonly SubscriptionManager _groups = new SubscriptionManager();
    private readonly SubscriptionManager _users = new SubscriptionManager();

    private readonly AckHandler _ackHandler;
    private int _internalId;

    private volatile bool _isInitialized;
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="NpgsqlHubLifetimeManager{THub}"/> class.
    /// </summary>
    /// <param name="logger">The logger for the hub lifetime manager.</param>
    /// <param name="options">Configuration options for the PostgreSQL backplane.</param>
    /// <param name="lifetime">The application lifetime to register startup tasks.</param>
    /// <param name="hubProtocolResolver">Resolver for hub protocols.</param>
    /// <param name="globalHubOptions">Global hub options configuration.</param>
    /// <param name="hubOptions">Hub-specific options configuration.</param>
    /// <exception cref="InvalidOperationException">Thrown when ConnectionString is not provided in options.</exception>
    public NpgsqlHubLifetimeManager(
        ILogger<NpgsqlHubLifetimeManager<THub>> logger,
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
        ArgumentNullException.ThrowIfNull(connectionId);

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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override Task SendGroupAsync(string groupName, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        if (groupName == null)
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationGroup, groupName, methodName, args, null);
        return PublishAsync(MessageType.InvocationGroup, message);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public override Task SendGroupExceptAsync(string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = new CancellationToken())
    {
        ArgumentNullException.ThrowIfNull(groupName);

        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationGroup, groupName, methodName, args, excludedConnectionIds);
        return PublishAsync(MessageType.InvocationGroup, message);
    }

    /// <inheritdoc />
    public override Task SendUserAsync(string userId, string methodName, object?[] args, CancellationToken cancellationToken = new CancellationToken())
    {
        var message = _protocol.WriteTargetedInvocation(MessageType.InvocationUser, userId, methodName, args, null);
        return PublishAsync(MessageType.InvocationUser, message);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    /// Releases all resources used by the <see cref="NpgsqlHubLifetimeManager{THub}"/>.
    /// This includes cancelling background tasks and disposing the data source.
    /// </summary>
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

    /// <summary>
    /// Sends a group action (add/remove) to other servers and waits for acknowledgment.
    /// </summary>
    /// <param name="connectionId">The ID of the connection to add/remove from the group.</param>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="action">The action to perform (Add or Remove).</param>
    /// <returns>A task that completes when the acknowledgment is received or times out.</returns>
    private async Task SendGroupActionAndWaitForAck(string connectionId, string groupName, GroupAction action)
    {
        var id = Interlocked.Increment(ref _internalId);
        var ack = _ackHandler.CreateAck(id);
        // Send Add/Remove Group to other servers and wait for an ack or timeout
        var message = _protocol.WriteGroupCommand(new PostgresGroupCommand(id, _serverName, action, groupName, connectionId));
        await PublishAsync(MessageType.Group, message);

        await ack;
    }

    /// <summary>
    /// Adds a connection to a group locally on this server instance.
    /// </summary>
    /// <param name="connection">The connection to add to the group.</param>
    /// <param name="groupName">The name of the group to add the connection to.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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

    /// <summary>
    /// Removes a connection from a group locally on this server instance.
    /// </summary>
    /// <param name="connection">The connection to remove from the group.</param>
    /// <param name="groupName">The name of the group to remove the connection from.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
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


    /// <summary>
    /// Ensures that the PostgreSQL database is initialized, including creating the necessary schema and tables.
    /// The following operations are performed:
    /// - Creates the specified schema if it does not exist.
    /// - Creates a table for storing messages if it does not exist.
    /// - Sets up a trigger and function to notify on new message inserts.
    /// - Starts background tasks to listen for notifications and process them.
    /// </summary>
    private async Task EnsurePostgresInitializedAsync()
    {
        if (_isInitialized) return;

        await _initSemaphore.WaitAsync(_cancellationTokenSource.Token);
        try
        {
            if (_isInitialized) return;

            var notificationChannel = _options.Value.NotificationChannel;
            _logger.LogInformation("Initializing PostgreSQL backplane with notification channel: {NotificationChannel}", notificationChannel);

            // Ensure schema exists and create message table in a single command
            var schema = _options.Value.SchemaName;
            var tableName = $"{_options.Value.TableSlugGenerator(typeof(THub))}_Messages";
            _logger.LogDebug("Creating schema '{Schema}' and table '{TableName}'", schema, tableName);

            var createSchemaAndTableSql = $@"
                CREATE SCHEMA IF NOT EXISTS ""{schema}"";
                CREATE TABLE IF NOT EXISTS ""{schema}"".""{tableName}"" (
                    ""Id"" SERIAL PRIMARY KEY,
                    ""Payload"" BYTEA NOT NULL,
                    ""InsertedOn"" TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                
                -- Drop existing trigger and function if they exist
                DROP TRIGGER IF EXISTS {tableName}_insert_trigger ON ""{schema}"".""{tableName}"";
                DROP FUNCTION IF EXISTS notify_{tableName}_change();
                
                -- Create a function to notify on new message inserts
                CREATE OR REPLACE FUNCTION notify_{tableName}_change()
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
                CREATE TRIGGER {tableName}_insert_trigger
                AFTER INSERT ON ""{schema}"".""{tableName}""
                FOR EACH ROW
                EXECUTE FUNCTION notify_{tableName}_change();
                ";

            await using var cmd = _dataSource.CreateCommand(createSchemaAndTableSql);
            await cmd.ExecuteNonQueryAsync(_cancellationTokenSource.Token);

            _logger.LogInformation("Database schema and triggers created successfully");

            // Start background tasks
            _logger.LogDebug("Starting background notification listener and processor tasks");
            _ = Task.Run(() => ListenForNotifications(_notificationChannel.Writer, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _ = Task.Run(() => ProcessNotifications(_notificationChannel.Reader, _cancellationTokenSource.Token), _cancellationTokenSource.Token);

            _isInitialized = true;
            _logger.LogInformation("PostgreSQL backplane initialization completed");
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// Listens for PostgreSQL notifications and forwards them to the notification channel.
    /// Implements retry logic with exponential backoff in case of connection failures.
    /// </summary>
    /// <param name="writer">The channel writer to send notifications to.</param>
    /// <param name="cancellationToken">Token to cancel the listening operation.</param>
    /// <returns>A task that represents the listening operation.</returns>
    private async Task ListenForNotifications(ChannelWriter<Notification> writer, CancellationToken cancellationToken)
    {
        var retryCount = 0;
        var maxRetryCount = _options.Value.MaxRetryAttempts;
        var baseDelay = _options.Value.BaseRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            NpgsqlConnection? conn = null;
            try
            {
                // Use the data source for connection pooling and better resource management
                conn = await _dataSource.OpenConnectionAsync(cancellationToken);

                _logger.LogDebug("PostgreSQL notification listener connection opened. State: {ConnectionState}", conn.State);

                // Set up the notification handler
                conn.Notification += (_, args) =>
                {
                    try
                    {
                        _logger.LogTrace("Received notification on channel '{Channel}' from PID {PID}", args.Channel, args.PID);

                        if (args.Channel == _options.Value.NotificationChannel)
                        {
                            ProcessNotificationPayload(args.Payload, writer, args.Channel, args.PID);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing PostgreSQL notification payload: {Payload}", args.Payload);
                    }
                };

                // Send the LISTEN command
                await using var cmd = new NpgsqlCommand($"LISTEN {_options.Value.NotificationChannel};", conn);

                // Apply command timeout if configured
                if (_options.Value.CommandTimeout.HasValue)
                {
                    cmd.CommandTimeout = (int)_options.Value.CommandTimeout.Value.TotalSeconds;
                }

                await cmd.ExecuteNonQueryAsync(cancellationToken);

                _logger.LogInformation("Successfully listening on PostgreSQL notification channel '{Channel}'", _options.Value.NotificationChannel);

                // Reset retry count on successful connection
                retryCount = 0;

                // Keep the connection alive and wait for notifications
                while (!cancellationToken.IsCancellationRequested)
                {
                    await conn.WaitAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("PostgreSQL notification listener cancelled");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PostgreSQL notification listener (attempt {RetryCount}/{MaxRetryCount})", retryCount + 1, maxRetryCount);

                // Implement exponential backoff with jitter
                if (!cancellationToken.IsCancellationRequested && retryCount < maxRetryCount)
                {
                    retryCount++;
                    var delay = TimeSpan.FromMilliseconds(
                        baseDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1) +
                        Random.Shared.Next(0, 1000));

                    _logger.LogWarning("Retrying PostgreSQL notification listener in {Delay}ms", delay.TotalMilliseconds);

                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                else if (retryCount >= maxRetryCount)
                {
                    _logger.LogCritical("Maximum retry attempts ({MaxRetryCount}) reached for PostgreSQL notification listener. Stopping listener.", maxRetryCount);
                    break;
                }
            }
            finally
            {
                // Ensure connection is properly disposed
                conn?.Dispose();
            }
        }

        _logger.LogInformation("PostgreSQL notification listener stopped");
    }

    /// <summary>
    /// Processes a notification payload received from PostgreSQL and converts it to a structured notification.
    /// </summary>
    /// <param name="payload">The JSON payload containing the message data.</param>
    /// <param name="writer">The channel writer to send the parsed notification to.</param>
    /// <param name="channel">The notification channel name.</param>
    /// <param name="pid">The PostgreSQL process ID that sent the notification.</param>
    private void ProcessNotificationPayload(string payload, ChannelWriter<Notification> writer, string channel, int pid)
    {
        try
        {
            // Parse the JSON payload that contains base64 encoded binary data
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var id = root.GetProperty("Id").GetInt32();
            var payloadBase64 = root.GetProperty("Payload").GetString();
            var insertedOn = root.GetProperty("InsertedOn").GetDateTime();

            if (payloadBase64 != null)
            {
                var payloadBytes = Convert.FromBase64String(payloadBase64);
                var message = new Message(id, payloadBytes, insertedOn);
                var notification = new Notification(message, channel, pid);

                if (!writer.TryWrite(notification))
                {
                    _logger.LogWarning("Failed to write notification to channel, channel may be full or closed");
                }
                else
                {
                    _logger.LogTrace("Successfully processed notification with ID {MessageId}", id);
                }
            }
            else
            {
                _logger.LogWarning("Notification payload missing or null: {Payload}", payload);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON payload: {Payload}", payload);
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to decode base64 payload: {Payload}", payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing notification payload: {Payload}", payload);
        }
    }

    /// <summary>
    /// Processes incoming notifications from the notification channel and dispatches them to the appropriate handlers.
    /// </summary>
    /// <param name="reader">The channel reader to receive notifications from.</param>
    /// <param name="cancellationToken">Token to cancel the processing operation.</param>
    /// <returns>A task that represents the processing operation.</returns>
    private async Task ProcessNotifications(ChannelReader<Notification> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var notification in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _logger.LogTrace("Processing incoming message: Id={MessageId}", notification.Message.Id);
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
            _logger.LogDebug("Notification processor cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in notification processor");
        }
    }

    /// <summary>
    /// Handles invocation messages targeted at specific users.
    /// </summary>
    /// <param name="payload">The message payload containing the user invocation data.</param>
    /// <returns>A task that represents the handling operation.</returns>
    private async Task HandleInvocationUser(ReadOnlyMemory<byte> payload)
    {
        var multiInvocation = _protocol.ReadTargetedInvocation(payload);
        var connections = _users.Get(multiInvocation.Target);
        var invocation = multiInvocation.Invocation;
        await ExecuteInvocation(invocation, connections);
    }

    /// <summary>
    /// Handles invocation messages targeted at specific connections.
    /// </summary>
    /// <param name="payload">The message payload containing the connection invocation data.</param>
    /// <returns>A task that represents the handling operation.</returns>
    private async Task HandleInvocationConnection(ReadOnlyMemory<byte> payload)
    {
        var connectionInvocation = _protocol.ReadTargetedInvocation(payload);
        var userConnection = _connections[connectionInvocation.Target];
        if (userConnection == null) return;
        await userConnection.WriteAsync(connectionInvocation.Invocation.Message);
    }

    /// <summary>
    /// Handles invocation messages targeted at specific groups.
    /// </summary>
    /// <param name="payload">The message payload containing the group invocation data.</param>
    /// <returns>A task that represents the handling operation.</returns>
    private async Task HandleInvocationGroup(ReadOnlyMemory<byte> payload)
    {
        var multiInvocation = _protocol.ReadTargetedInvocation(payload);
        var connections = _groups.Get(multiInvocation.Target);
        var invocation = multiInvocation.Invocation;
        await ExecuteInvocation(invocation, connections);
    }

    /// <summary>
    /// Handles invocation messages targeted at all connections.
    /// </summary>
    /// <param name="payload">The message payload containing the broadcast invocation data.</param>
    /// <returns>A task that represents the handling operation.</returns>
    private async Task HandleInvocationAll(ReadOnlyMemory<byte> payload)
    {
        var invocation = _protocol.ReadInvocationAll(payload);
        await ExecuteInvocation(invocation, _connections);
    }

    /// <summary>
    /// Handles group management commands (add/remove connections from groups).
    /// </summary>
    /// <param name="payload">The message payload containing the group command data.</param>
    /// <returns>A task that represents the handling operation.</returns>
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

    /// <summary>
    /// Handles acknowledgment messages from other servers.
    /// </summary>
    /// <param name="payload">The message payload containing the acknowledgment data.</param>
    /// <returns>A task that represents the handling operation.</returns>
    private Task HandleAck(ReadOnlyMemory<byte> payload)
    {
        var ack = _protocol.ReadAck(payload);
        if (ack.ServerName != _serverName) return Task.CompletedTask;
        _ackHandler.TriggerAck(ack.Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Executes an invocation on all specified connections, excluding any specified connection IDs.
    /// </summary>
    /// <param name="invocation">The invocation to execute.</param>
    /// <param name="connections">The connections to send the invocation to.</param>
    /// <returns>A task that represents the execution operation.</returns>
    private static async Task ExecuteInvocation(PostgresInvocation invocation, HubConnectionStore? connections)
    {
        if (connections == null) return;
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

    /// <summary>
    /// Generates a unique server name for this server instance.
    /// Combines the machine name with a GUID for uniqueness across deployments.
    /// </summary>
    /// <returns>A unique server name string.</returns>
    private static string GenerateServerName()
    {
        // Use the machine name for convenient diagnostics, but add a guid to make it unique.
        // Example: MyServerName_02db60e5fab243b890a847fa5c4dcb29
        return $"{Environment.MachineName}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// Feature interface for tracking SignalR groups associated with a connection.
    /// </summary>
    private interface ISqlFeature
    {
        /// <summary>
        /// Gets the collection of group names that this connection belongs to.
        /// </summary>
        HashSet<string> Groups { get; }
    }

    /// <summary>
    /// Implementation of <see cref="ISqlFeature"/> that tracks SignalR groups for a connection.
    /// </summary>
    private class SqlFeature : ISqlFeature
    {
        /// <inheritdoc />
        public HashSet<string> Groups { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Represents a message stored in the PostgreSQL database.
    /// </summary>
    /// <param name="Id">The unique identifier of the message.</param>
    /// <param name="Payload">The binary payload containing the serialized message data.</param>
    /// <param name="InsertedOn">The timestamp when the message was inserted into the database.</param>
    private record Message(int Id, byte[] Payload, DateTime InsertedOn);

    /// <summary>
    /// Represents a notification received from PostgreSQL's LISTEN/NOTIFY mechanism.
    /// </summary>
    /// <param name="Message">The message data extracted from the notification.</param>
    /// <param name="Channel">The notification channel name.</param>
    /// <param name="PID">The PostgreSQL process ID that sent the notification.</param>
    private record Notification(Message Message, string Channel, int PID);

    /// <summary>
    /// Publishes a message to the PostgreSQL backplane by inserting it into the messages table.
    /// The database trigger will automatically notify other server instances.
    /// </summary>
    /// <param name="type">The type of message being published.</param>
    /// <param name="payload">The serialized message payload.</param>
    /// <returns>A task that represents the publishing operation.</returns>
    private async Task PublishAsync(MessageType type, byte[] payload)
    {
        try
        {
            // Only ensure initialization on first call, prevent recursion
            if (!_isInitialized)
            {
                _logger.LogDebug("Initializing PostgreSQL backplane before publishing");
                await EnsurePostgresInitializedAsync();
            }

            _logger.LogTrace("Publishing message of type {MessageType}", type);

            // Use the shared data source for connection pooling
            var schema = _options.Value.SchemaName;
            var tableName = $"{_options.Value.TableSlugGenerator(typeof(THub))}_Messages";

            var insertSql = $@"INSERT INTO ""{schema}"".""{tableName}"" (""Payload"") VALUES (@payload)";

            _logger.LogTrace("Executing SQL: {InsertSql}", insertSql);
            await using var cmd = _dataSource.CreateCommand(insertSql);
            cmd.Parameters.AddWithValue("@payload", payload);
            var rowsAffected = await cmd.ExecuteNonQueryAsync(_cancellationTokenSource.Token);
            _logger.LogTrace("Message inserted successfully. Rows affected: {RowsAffected}", rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message of type {MessageType}", type);
            throw;
        }
    }
}
