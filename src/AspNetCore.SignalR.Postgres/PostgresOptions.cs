// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations;

namespace AspNetCore.SignalR.Postgres
{
    /// <summary>
    /// Options used to configure <see cref="PostgresHubLifetimeManager{THub}"/>.
    /// </summary>
    public class PostgresOptions
    {
        /// <summary>
        /// Shared lock to prevent multiple concurrent installs against the same DB.
        /// This prevents auto-enable of service broker from deadlocking 
        /// when an application has multiple hubs.
        /// </summary>
        internal readonly SemaphoreSlim InstallLock = new SemaphoreSlim(1);

        /// <summary>
        /// The Postgres connection string to use.
        /// </summary>
        [Required]
        public string ConnectionString { get; set; } = "";

        /// <summary>
        /// The number of tables to store messages in. Using more tables reduces lock contention and may increase throughput.
        /// This must be consistent between all nodes in the web farm.
        /// Defaults to 1.
        /// </summary>
        public int TableCount { get; set; } = 1;

        /// <summary>
        /// The name of the database schema to use for the underlying Postgres Tables.
        /// </summary>
        public string SchemaName { get; set; } = "SignalR";

        /// <summary>
        /// Function that determines the part of the Postgres table name that identifies the Hub.
        /// It should be assumed that 15 characters of Postgres's 128 character max are not available for use.
        /// By default, uses the Hub's unqualified type name.
        /// </summary>
        public Func<Type, string> TableSlugGenerator { get; set; } = type => type.Name;

        /// <summary>
        /// If true, on startup the application will attempt to automatically enable Postgres Service Broker.
        /// Service Broker allows for more performant operation. It can be manually enabled on the server with
        /// "ALTER DATABASE [DatabaseName] SET ENABLE_BROKER". It requires an exclusive lock on the database.
        /// </summary>
        public bool AutoEnableServiceBroker { get; set; } = false;

        /// <summary>
        /// <para>
        /// If true (the default), on startup the application will attempt to automatically install its 
        /// required tables into the target database. If disabled, you are required to install the tables yourself
        /// using the <see href="https://github.com/IntelliTect/IntelliTect.AspNetCore.SignalR.Postgres/blob/master/src/IntelliTect.AspNetCore.SignalR.Postgres/Internal/Postgres/install.sql">install.sql</see>
        /// script in this project's repository, changing the @SCHEMA_NAME, @MESSAGE_TABLE_COUNT, 
        /// and @MESSAGE_TABLE_NAME variables to match your configuration. 
        /// </para>
        /// </summary>
        public bool AutoInstallSchema { get; set; } = true;

        /// <summary>
        /// Flag enum that specifies the allowed modes for retrieving messages from Postgres. Default Auto.
        /// </summary>
        public PostgresMessageMode Mode { get; set; } = PostgresMessageMode.Auto;
    }
}