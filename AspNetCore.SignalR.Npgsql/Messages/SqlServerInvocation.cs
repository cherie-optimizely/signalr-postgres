using Microsoft.AspNetCore.SignalR;
namespace AspNetCore.SignalR.Npgsql.Messages
{

    internal readonly struct SqlServerInvocation
    {
        /// <summary>
        /// Gets a list of connections that should be excluded from this invocation.
        /// May be null to indicate that no connections are to be excluded.
        /// </summary>
        public IReadOnlyList<string>? ExcludedConnectionIds { get; }

        /// <summary>
        /// Gets the message serialization cache containing serialized payloads for the message.
        /// </summary>
        public SerializedHubMessage Message { get; }

        public SqlServerInvocation(SerializedHubMessage message, IReadOnlyList<string>? excludedConnectionIds)
        {
            Message = message;
            ExcludedConnectionIds = excludedConnectionIds;
        }
    }
}
