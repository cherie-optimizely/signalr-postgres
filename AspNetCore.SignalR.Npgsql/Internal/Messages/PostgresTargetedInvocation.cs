namespace AspNetCore.SignalR.Npgsql.Messages
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
