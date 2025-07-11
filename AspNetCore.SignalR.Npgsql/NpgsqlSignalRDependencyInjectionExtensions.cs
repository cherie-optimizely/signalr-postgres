using AspNetCore.SignalR.Npgsql;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.Extensions.DependencyInjection;

public static class NpgsqlSignalRDependencyInjectionExtensions
{
    public static ISignalRServerBuilder AddPostgres(this ISignalRServerBuilder signalrBuilder)
    {
        return AddPostgres(signalrBuilder, o => { });
    }

    public static ISignalRServerBuilder AddPostgres(this ISignalRServerBuilder signalrBuilder, string connectionString)
    {
        return AddPostgres(signalrBuilder, o =>
        {
            o.ConnectionString = connectionString;
        });
    }

    public static ISignalRServerBuilder AddPostgres(this ISignalRServerBuilder signalrBuilder, Action<PostgresOptions> configure)
    {
        signalrBuilder.Services.Configure(configure);
        signalrBuilder.Services.AddSingleton(typeof(HubLifetimeManager<>), typeof(NpgsqlHubLifetimeManager<>));
        return signalrBuilder;
    }

    public static ISignalRServerBuilder AddPostgres(this ISignalRServerBuilder signalrBuilder, string connectionString, Action<PostgresOptions> configure)
    {
        return AddPostgres(signalrBuilder, o =>
        {
            o.ConnectionString = connectionString;
            configure(o);
        });
    }
}


