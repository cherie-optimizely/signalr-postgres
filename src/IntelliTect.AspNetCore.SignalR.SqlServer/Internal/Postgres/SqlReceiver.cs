// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Npgsql;

namespace IntelliTect.AspNetCore.SignalR.SqlServer.Internal.Postgres
{
    internal class SqlReceiver : IDisposable
    {
        private readonly Tuple<int, int>[] _updateLoopRetryDelays = new[] {
            Tuple.Create(0, 3),    // 0ms x 3
            Tuple.Create(10, 3),   // 10ms x 3
            Tuple.Create(50, 2),   // 50ms x 2
            Tuple.Create(100, 2),  // 100ms x 2
            Tuple.Create(200, 2),  // 200ms x 2
            Tuple.Create(1000, 2), // 1000ms x 2
            Tuple.Create(1500, 2), // 1500ms x 2
            Tuple.Create(3000, 1)  // 3000ms x 1
        };
        private readonly static TimeSpan _dependencyTimeout = TimeSpan.FromSeconds(60);

        private CancellationTokenSource _cts = new();
        private bool _notificationsDisabled;

        private readonly SqlServerOptions _options;
        private readonly string _tableName;
        private readonly ILogger _logger;
        private readonly string _tracePrefix;

        private int? _lastPayloadId = null;
        private Func<long, byte[], Task>? _onReceived = null;
        private bool _disposed;
        private readonly string _maxIdSql = "SELECT [PayloadId] FROM [{0}].[{1}_Id]";
        private readonly string _selectSql = "SELECT [PayloadId], [Payload], [InsertedOn] FROM [{0}].[{1}] WHERE [PayloadId] > @PayloadId";

        public SqlReceiver(SqlServerOptions options, ILogger logger, string tableName, string tracePrefix)
        {
            _options = options;
            _tableName = tableName;
            _tracePrefix = tracePrefix;
            _logger = logger;

            _maxIdSql = String.Format(CultureInfo.InvariantCulture, _maxIdSql, _options.SchemaName, _tableName);
            _selectSql = String.Format(CultureInfo.InvariantCulture, _selectSql, _options.SchemaName, _tableName);
        }

        public Task Start(Func<long, byte[], Task> onReceived)
        {
            if (_disposed) throw new ObjectDisposedException(null);

            _cts.Cancel();
            _cts.Dispose();
            _cts = new();

            _onReceived = onReceived;

            return Task.Factory
                .StartNew(StartLoop, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach)
                .Unwrap();
        }

        private async Task StartLoop()
        {
            if (_cts.IsCancellationRequested) return;

            if (!_lastPayloadId.HasValue)
            {
                _lastPayloadId = await GetLastPayloadId();
            }

            if (_cts.IsCancellationRequested) return;

            if (_options.Mode.HasFlag(SqlServerMessageMode.ServiceBroker))// && StartSqlDependencyListener())
            {
                throw new NotImplementedException("Service Broker mode is not yet implemented for Postgres.");
            }
            else if (_options.Mode.HasFlag(SqlServerMessageMode.Polling))
            {
                await PollingLoop(_cts.Token);
            }
            else
            {
                throw new InvalidOperationException("None of the configured SqlServerMessageMode are suitable for use.");
            }
        }

        /// <summary>
        /// Loop until cancelled, using SQL service broker notifications to watch for new rows.
        /// </summary>
        // private async Task NotificationLoop(CancellationToken cancellationToken)
        // {
           
        // }

        /// <summary>
        /// Loop until cancelled, using periodic queries to watch for new rows.
        /// </summary>
        private async Task PollingLoop(CancellationToken cancellationToken)
        {
            var delays = _updateLoopRetryDelays;
            for (var retrySetIndex = 0; retrySetIndex < delays.Length; retrySetIndex++)
            {
                Tuple<int, int> retry = delays[retrySetIndex];
                var retryDelay = retry.Item1;
                var numRetries = retry.Item2;

                for (var retryIndex = 0; retryIndex < numRetries; retryIndex++)
                {
                    if (cancellationToken.IsCancellationRequested) return;

                    var recordCount = 0;
                    try
                    {
                        if (retryDelay > 0)
                        {
                            _logger.LogTrace("{HubStream}: Waiting {1}ms before checking for messages again", _tracePrefix, retryDelay);

                            await Task.Delay(retryDelay, cancellationToken);
                        }

                        recordCount = await ReadRows(null);
                    }
                    catch (TaskCanceledException) { return; }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{HubStream}: Error in SQL polling loop", _tracePrefix);
                    }

                    if (recordCount > 0)
                    {
                        _logger.LogDebug("{HubStream}: {RecordCount} records received", _tracePrefix, recordCount);

                        // We got records so start the retry loop again
                        // at the lowest delay.
                        retrySetIndex = -1;
                        break;
                    }

                    _logger.LogTrace("{HubStream}: No records received", _tracePrefix);

                    var isLastRetry = retrySetIndex == delays.Length - 1 && retryIndex == numRetries - 1;

                    if (isLastRetry)
                    {
                        // Last retry loop so just stay looping on the last retry delay
                        retryIndex--;
                    }
                }
            }

            _logger.LogDebug("{HubStream}: SQL polling loop fell out", _tracePrefix);
            await StartLoop();
        }

        /// <summary>
        /// Fetch the starting payloadID that will be used to query for newer messages.
        /// </summary>
        private async Task<int> GetLastPayloadId()
        {
            try
        {
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(_options.ConnectionString);
                var dataSource = dataSourceBuilder.Build();

                var connection = await dataSource.OpenConnectionAsync();

                await using (var command = new NpgsqlCommand("SELECT max(\"PayloadId\") FROM \"SignalR\".\"Messages\";", connection))
                {
                    var id = await command.ExecuteScalarAsync();
                    if (id is null) throw new Exception($"Unable to retrieve the starting payload ID for table \"Messages\"");
                    return (int) id;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{HubStream}: SqlReceiver error starting", _tracePrefix);
                throw;
            }
        }

        /// <summary>
        /// Execute a query against the database to look for rows newer than <see cref="_lastPayloadId"/>
        /// </summary>
        /// <param name="beforeExecute"></param>
        /// <returns></returns>
        private async Task<int> ReadRows(Action<NpgsqlCommand>? beforeExecute)
        {
            var recordCount = 0;

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_options.ConnectionString);
            var dataSource = dataSourceBuilder.Build();

            var connection = await dataSource.OpenConnectionAsync();


            await using var command = dataSource.CreateCommand("SELECT \"PayloadId\", \"Payload\", \"InsertedOn\" FROM \"SignalR\".\"Messages\" WHERE \"PayloadId\" > (@p);");
            command.Parameters.AddWithValue("p", _lastPayloadId ?? 0);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                recordCount++;
                await ProcessRecord(reader);
            }

            return recordCount;
        }

        /// <summary>
        /// Process a message row received from the database.
        /// </summary>
        /// <param name="record"></param>
        private async Task ProcessRecord(IDataRecord record)
        {
            var id = record.GetInt32(0);
            var payload = ((NpgsqlDataReader)record)[1] as byte[]; // Assuming the payload is a byte array

            _logger.LogTrace("{HubStream}: SqlReceiver last payload ID={1}, new payload ID={2}", _tracePrefix, _lastPayloadId, id);

            if (id > _lastPayloadId + 1)
            {
                _logger.LogError("{HubStream}: Missed message(s) from SQL Server. Expected payload ID {1} but got {2}.", _tracePrefix, _lastPayloadId + 1, id);
            }
            else if (id <= _lastPayloadId)
            {
                _logger.LogInformation("{HubStream}: Duplicate message(s) or payload ID reset from SQL Server. Last payload ID {1}, this payload ID {2}", _tracePrefix, _lastPayloadId, id);
            }

            _lastPayloadId = id;

            _logger.LogTrace("{HubStream}: Updated receive reader initial payload ID parameter={1}", _tracePrefix, _lastPayloadId);

            _logger.LogTrace("{HubStream}: Payload {1} received", _tracePrefix, id);

            await _onReceived!.Invoke(id, payload ?? Array.Empty<byte>());
        }

        /// <summary>
        /// Attempt to start SQL Dependency listening for notification-based polling,
        /// returning a boolean indicating success. If false, SQL notifications cannot be used.
        /// </summary>
        /// <returns></returns>
        // private bool StartSqlDependencyListener()
        // {
        //     if (_notificationsDisabled)
        //     {
        //         return false;
        //     }

        //     _logger.LogTrace("{HubStream}: Starting SQL notification listener", _tracePrefix);
        //     try
        //     {
        //         if (SqlDependency.Start(_options.ConnectionString))
        //         {
        //             _logger.LogTrace("{HubStream}: SQL notification listener started", _tracePrefix);
        //         }
        //         else
        //         {
        //             _logger.LogTrace("{HubStream}: SQL notification listener was already running", _tracePrefix);
        //         }
        //         return true;
        //     }
        //     catch (InvalidOperationException)
        //     {
        //         _logger.LogWarning("{HubStream}: SQL Service Broker is disabled on the target database.", _tracePrefix);
        //         _notificationsDisabled = true;
        //         return false;
        //     }
        //     catch (NullReferenceException)
        //     {
        //         // Workaround for https://github.com/dotnet/SqlClient/issues/1264

        //         _logger.LogWarning("{HubStream}: SQL Service Broker is disabled or unsupported by the target database.", _tracePrefix);
        //         _notificationsDisabled = true;
        //         return false;
        //     }
        //     catch (SqlException ex) when (ex.Number == 40510 || ex.Message.Contains("not supported"))
        //     {
        //         // Workaround for https://github.com/dotnet/SqlClient/issues/1264.
        //         // Specifically that Azure SQL Database reports that service broker is enabled,
        //         // even though it is entirely unsupported.

        //         _logger.LogWarning("{HubStream}: SQL Service Broker is unsupported by the target database.", _tracePrefix);
        //         _notificationsDisabled = true;
        //         return false;
        //     }
        //     catch (Exception ex)
        //     {
        //         _logger.LogError(ex, "{HubStream}: Error starting SQL notification listener", _tracePrefix);

        //         return false;
        //     }
        // }

        public void Dispose()
        {
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
