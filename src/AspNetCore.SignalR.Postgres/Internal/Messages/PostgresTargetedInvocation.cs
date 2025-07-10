using AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AspNetCore.SignalR.Postgres.Internal.Messages
{

    internal readonly struct PostgresTargetedInvocation
    {
        public string Target { get; }

        public PostgresInvocation Invocation { get; }

        public PostgresTargetedInvocation(string target, PostgresInvocation invocation)
        {
            Target = target;
            Invocation = invocation;
        }
    }
}
