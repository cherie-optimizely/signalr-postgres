// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace AspNetCore.SignalR.Postgres
{
    /// <summary>
    /// Specifies the allowed modes of acquiring scaleout messages from Postgres.
    /// </summary>
    [Flags]
    public enum PostgresMessageMode
    {
        /// <summary>
        /// Use Postgres Service Broker for discovering when new messages are available.
        /// </summary>
        ServiceBroker = 1 << 0,

        /// <summary>
        /// Use periodic polling to discover when new messages are available.
        /// </summary>
        Polling = 1 << 1,

        /// <summary>
        /// Use the most suitable mode for acquiring messages.
        /// </summary>
        Auto = ServiceBroker | Polling,
    }
}
