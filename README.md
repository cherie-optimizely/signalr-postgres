# AspNetCore.SignalR.SqlServer

A Postgres backplane for ASP.NET Core SignalR.

This project is a fork of
the [SignalR Core SQL Server provider](https://github.com/IntelliTect/IntelliTect.AspNetCore.SignalR.SqlServer),
reworked to use Postgres as the persistent layer, offering periodic polling and SQL Notify for receiving messages.

## Postgres Configuration

## Usage

1. Install the `AspNetCore.SignalR.Postgres` NuGet package.
2. In `ConfigureServices` in `Startup.cs`, configure SignalR with `.UsePostgres()`:

Simple configuration:

``` cs
services
    .AddSignalR()
    .AddPostgres(Configuration.GetConnectionString("Default"));
```

Advanced configuration:

``` cs 
services
    .AddSignalR()
    .AddPostgres(o =>
    {
        o.ConnectionString = Configuration.GetConnectionString("Default");
        o.SchemaName = "SignalRCore";
    });
```

Alternatively, you may configure `AspNetCore.SignalR.Postgres.PostgresOptions`
with [the Options pattern](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-5.0).

``` cs
services.Configure<PostgresOptions>(Configuration.GetSection("SignalR:Postgres"));
```

## Performance

This project is still in experimental stages, and is not yet recommended for production use.

## Development
To test this locally, include the following in the `.csproj` file:

``` xml
<ItemGroup>
    ...
    <ProjectReference Include="../../../signalr-postgres/src/IntelliTect.AspNetCore.SignalR.SqlServer/IntelliTect.AspNetCore.SignalR.SqlServer.csproj" />
</ItemGroup>
```

## License

[Apache 2.0](./LICENSE.txt).

Credit to Microsoft for both Microsoft.AspNet.SignalR.SqlServer and Microsoft.AspNetCore.SignalR.StackExchangeRedis,
upon which this project is based.
