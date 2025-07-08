// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntelliTect.AspNetCore.SignalR.SqlServer.Internal.Postgres
{
    internal class SqlInstaller(SqlServerOptions options, ILogger logger, string messagesTableNamePrefix, string tracePrefix)
    {
        private const int SchemaVersion = 1;

        public async Task Install()
        {
            if (!options.AutoInstallSchema)
            {
                logger.LogInformation("{HubName}: Skipping install of SignalR SQL objects", tracePrefix);
                return;
            }

            await options.InstallLock.WaitAsync();
            logger.LogInformation("{HubName}: Start installing SignalR SQL objects", tracePrefix);
            try
            {
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.ConnectionString);
                var dataSource = dataSourceBuilder.Build();

                var connection = await dataSource.OpenConnectionAsync();

                if (options.AutoInstallSchema)
                {

                    var script = GetType().Assembly.StringResource("install.sql");

                    // Insert some data
                    await using (var command = new NpgsqlCommand(script, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }

                    logger.LogInformation("{HubName}: SignalR SQL objects installed", messagesTableNamePrefix);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{HubName}: Unable to install SignalR SQL objects", messagesTableNamePrefix);
                throw;
            }
            finally
            {
                options.InstallLock.Release();
            }
        }
    }
}
