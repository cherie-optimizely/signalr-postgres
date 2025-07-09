// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntelliTect.AspNetCore.SignalR.SqlServer.Internal.Postgres
{
    internal class SqlSender
    {
        private readonly string _insertDml;
        private readonly ILogger _logger;
        private readonly SqlServerOptions _options;

        public SqlSender(SqlServerOptions options, ILogger logger, string tableName)
        {
            _options = options;
            _insertDml = GetType().Assembly.StringResource("send.sql");
            _logger = logger;
        }

        public async Task Send(byte[] message)
        {
            var connection = new NpgsqlConnection(_options.ConnectionString);

            await connection.OpenAsync();

            await using (var command = new NpgsqlCommand(_insertDml, connection))
            {
                command.Parameters.AddWithValue("payload", message);
                await command.ExecuteNonQueryAsync();
            }
        }
    }
}
