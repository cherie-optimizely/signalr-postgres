using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
namespace AspNetCore.SignalR.Npgsql;

public static class NpgsqlSignalRDependencyInjectionExtensions
{
    public static ISignalRServerBuilder AddNpgsql(this ISignalRServerBuilder signalrBuilder)
    {
        return AddNpgsql(signalrBuilder, o => { });
    }
    private static ISignalRServerBuilder AddNpgsql(ISignalRServerBuilder signalrBuilder, Action<NpgsqlOption> configure)
    {
        signalrBuilder.Services.Configure(configure);
        signalrBuilder.Services.AddSingleton(typeof(HubLifetimeManager<>), typeof(PostgresHubLifetimeManager<>));
        return signalrBuilder;
    }
    
    public static ISignalRServerBuilder AddNpgsql(this ISignalRServerBuilder signalrBuilder, string connectionString)
    {
        return AddNpgsql(signalrBuilder, o =>
        {
            o.ConnectionString = connectionString;
        });
    }
    
    public static ISignalRServerBuilder AddNpgsql(this ISignalRServerBuilder signalrBuilder, string connectionString, Action<NpgsqlOption> configure)
    {
        return AddNpgsql(signalrBuilder, o =>
        {
            o.ConnectionString = connectionString;
            configure(o);
        });
    }
}

public class NpgsqlOption
{
    /// <summary>
    /// The Npgsql connection string to use.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = "";
    /// <summary>
    /// The name of the database schema to use for the underlying PostgresSQL Tables.
    /// </summary>
    public string SchemaName { get; set; } = "SignalR";
    
    /// <summary>
    /// Function that determines the part of the SQL Server table name that identifies the Hub.
    /// It should be assumed that 15 characters of SQL Server's 128 character max are not available for use.
    /// By default, uses the Hub's unqualified type name.
    /// </summary>
    public Func<Type, string> TableSlugGenerator { get; set; } = type => type.Name;
    public string NotificationChannel { get; set; } = "SignalRNotificationChannel";
}
