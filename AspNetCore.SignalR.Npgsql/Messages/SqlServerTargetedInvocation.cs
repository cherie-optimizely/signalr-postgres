namespace AspNetCore.SignalR.Npgsql.Messages
{

    internal readonly struct SqlServerTargetedInvocation
    {
        public string Target { get; }

        public SqlServerInvocation Invocation { get; }

        public SqlServerTargetedInvocation(string target, SqlServerInvocation invocation)
        {
            Target = target;
            Invocation = invocation;
        }
    }
}
