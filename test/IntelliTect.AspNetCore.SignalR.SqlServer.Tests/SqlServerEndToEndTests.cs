using IntelliTect.AspNetCore.SignalR.SqlServer.Internal.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using System.Collections.Concurrent;
using Xunit;

namespace IntelliTect.AspNetCore.SignalR.SqlServer.Tests
{
    public class SqlServerEndToEndTests
    {
        private const string databaseName = "SignalRUnitTestsDb";
        private const string username = "postgres";

        private const string password = "Pa55word";
        private const string connectionString = 
            // "Server=localhost,1433;Database=" + databaseName + ";User Id=sa;Password=" + password + ";TrustServerCertificate=true";
            "Host=34.52.228.243:5432;Username=" + username + ";Password=" + password + ";Database=" + databaseName + ";";

        [Fact(Skip = "Not supported.")]
        public async Task CanSendAndReceivePayloads_WithServiceBroker()
        {
            await CreateDatabaseAsync();

            var options = new PostgresOptions
            {
                ConnectionString = connectionString,
                AutoEnableServiceBroker = true,
                Mode = SqlServerMessageMode.ServiceBroker
            };

            var prefix = nameof(CanSendAndReceivePayloads_WithServiceBroker);
            await RunCore(options, prefix);
        }

        [SkippableFact]
        public async Task CanSendAndReceivePayloads_WithPolling()
        {
            await CreateDatabaseAsync();

            var options = new PostgresOptions
            {
                ConnectionString = connectionString,
                Mode = SqlServerMessageMode.Polling
            };

            var prefix = nameof(CanSendAndReceivePayloads_WithPolling);
            await RunCore(options, prefix);
        }


        [Fact(Skip = "Not supported.")]
        public async Task CanSendAndReceivePayloads_WithServiceBroker_UnderHeavyLoad()
        {
            await CreateDatabaseAsync();

            var options = new PostgresOptions
            {
                ConnectionString = connectionString,
                AutoEnableServiceBroker = true,
                Mode = SqlServerMessageMode.ServiceBroker
            };

            var prefix = nameof(CanSendAndReceivePayloads_WithServiceBroker_UnderHeavyLoad);
            var installer = new SqlInstaller(options, NullLogger.Instance, prefix, prefix);
            var receiver = new SqlReceiver(options, NullLogger.Instance, prefix + "_0", "");

            var receivedMessages = new ConcurrentBag<byte[]>();
            await installer.Install();
            var receiverTask = receiver.Start((_, message) =>
            {
                receivedMessages.Add(message);
                return Task.CompletedTask;
            });
            // Give the receiver time to reach a steady state (waiting).
            await Task.Delay(150);

            var cts = new CancellationTokenSource();

            // This is roughly analagous to number of connections, not number of servers.
            // The reasoning is that each connected client to the hub could be triggering
            // the hub to be sending messages.
            int numSenders = 100;
            int numSent = 0;
            var sender = new SqlSender(options, NullLogger.Instance, prefix + "_0");
            for (int i = 0; i < numSenders; i++)
            {
                _ = Task.Run(async () =>
                  {
                      var random = new Random();
                      while (!cts.IsCancellationRequested)
                      {
                          var payload = new byte[255];
                          random.NextBytes(payload);
                          await sender.Send(payload);
                          Interlocked.Increment(ref numSent);
                      }
                  }, cts.Token);
            }

            var payload = new byte[255];
            new Random().NextBytes(payload);
            await Task.Delay(10000);
            cts.Cancel();

            // Give the receiver time to reach a steady state (waiting).
            await Task.Delay(1000);

            Assert.Equal(numSent, receivedMessages.Count);

            receiver.Dispose();
            await receiverTask;
        }

        private async Task RunCore(PostgresOptions options, string prefix)
        {
            var installer = new SqlInstaller(options, NullLogger.Instance, prefix, prefix);
            var sender = new SqlSender(options, NullLogger.Instance, prefix + "_0");
            var receiver = new SqlReceiver(options, NullLogger.Instance, prefix + "_0", "");

            var receivedMessages = new ConcurrentBag<byte[]>();
            var receivedEvent = new SemaphoreSlim(0);
            await installer.Install();
            var receiverTask = receiver.Start((_, message) =>
            {
                receivedMessages.Add(message);
                receivedEvent.Release();
                return Task.CompletedTask;
            });
            // Give the receiver time to reach a steady state (waiting).
            await Task.Delay(150);

            var payload = new byte[255];
            new Random().NextBytes(payload);
            await sender.Send(payload);

            await receivedEvent.WaitAsync();
            var message = Assert.Single(receivedMessages);
            Assert.Equal(payload, message);

            // Give the receiver time to reach a steady state (waiting).
            await Task.Delay(50);

            receiver.Dispose();
            await receiverTask;
        }

        private static async Task CreateDatabaseAsync()
        {
        //     try
        //     {
        //         var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        //         var dataSource = dataSourceBuilder.Build();

        //         var connection = await dataSource.OpenConnectionAsync();
        //         await connection.OpenAsync();
        //         using var command = connection.CreateCommand();
        //         command.CommandText = $@"IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '{databaseName}')
        //             BEGIN CREATE DATABASE {databaseName}; END";
        //         await command.ExecuteNonQueryAsync();
        //     }
        //     catch (SqlException ex) when (
        //         ex.Number == 53 
        //         || ex.Message.Contains("Could not open a connection to SQL Server")
        //         || ex.Message.Contains("The server was not found or was not accessible")
        //     )
        //     {
        //         Skip.If(true, ex.Message);
        //     }
        }
    }
}
