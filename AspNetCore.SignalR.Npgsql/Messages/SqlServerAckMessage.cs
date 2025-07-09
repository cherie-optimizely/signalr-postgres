namespace AspNetCore.SignalR.Npgsql.Messages
{
    internal readonly struct SqlServerAckMessage
    {
        public int Id { get; }

        public string ServerName { get; }

        public SqlServerAckMessage(int id, string serverName)
        {
            Id = id;
            ServerName = serverName;
        }
    }
}
