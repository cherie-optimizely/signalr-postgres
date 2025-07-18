﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Npgsql;

namespace AspNetCore.SignalR.Postgres.Internal.Polling
{
    internal class SqlSender
    {
        private readonly string _insertDml;
        private readonly ILogger _logger;
        private readonly PostgresOptions _options;

        public SqlSender(PostgresOptions options, ILogger logger, string tableName)
        {
            _options = options;
            _insertDml = GetType().Assembly.StringResource("send.sql");
            _logger = logger;
        }

        public async Task Send(byte[] message)
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(_options.ConnectionString);
            var dataSource = dataSourceBuilder.Build();

            var connection = await dataSource.OpenConnectionAsync();

            await using (var command = new NpgsqlCommand(_insertDml, connection))
            {
                command.Parameters.AddWithValue("payload", message);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
