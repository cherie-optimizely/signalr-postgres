namespace AspNetCore.SignalR.Npgsql.Messages
{
    internal readonly struct PostgresAckMessage
    {
        public int Id { get; }

        public string ServerName { get; }

        public PostgresAckMessage(int id, string serverName)
        {
            Id = id;
            ServerName = serverName;
        }
    }
}
