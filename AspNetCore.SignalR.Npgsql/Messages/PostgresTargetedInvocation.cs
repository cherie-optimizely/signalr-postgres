namespace AspNetCore.SignalR.Npgsql.Messages
{

    internal readonly struct PostgresTargetedInvocation
    {
        public string Target { get; }

        public SqlServerInvocation Invocation { get; }

        public PostgresTargetedInvocation(string target, SqlServerInvocation invocation)
        {
            Target = target;
            Invocation = invocation;
        }
    }
}
