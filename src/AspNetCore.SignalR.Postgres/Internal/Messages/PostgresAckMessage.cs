using AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AspNetCore.SignalR.Postgres.Internal.Messages
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
