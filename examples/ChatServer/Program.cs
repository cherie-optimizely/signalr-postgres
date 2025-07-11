using AspNetCore.SignalR.Npgsql;
using ChatServer.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Configure SignalR with PostgreSQL backplane
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Host=localhost;Database=signalr_demo;Username=postgres;Password=postgres";

builder.Services.AddSignalR()
    .AddPostgres(connectionString, options =>
    {
        options.SchemaName = "signalr";
        options.TableSlugGenerator = hubType => hubType.Name.ToLowerInvariant();
        options.NotificationChannel = "chat_notifications";
        options.MaxRetryAttempts = 5;
        options.BaseRetryDelay = TimeSpan.FromSeconds(2);
        options.CommandTimeout = TimeSpan.FromSeconds(30);
    });

// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// Configure middleware
app.UseCors("AllowAll");

app.MapHub<ChatHub>("/chathub");

// Simple health check endpoint
app.MapGet("/", () => "ChatServer is running! Connect to /chathub for SignalR.");

app.MapGet("/health", () => new { Status = "Healthy", Server = Environment.MachineName, Timestamp = DateTime.UtcNow });

app.Run();
