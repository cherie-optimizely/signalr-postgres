using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace ChatClient;

public class Program
{
    private static readonly CancellationTokenSource _cancellationTokenSource = new();
    private static HubConnection? _connection;
    private static string? _userName;

    public static async Task Main(string[] args)
    {
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _cancellationTokenSource.Cancel();
        };

        Console.WriteLine("=== SignalR Chat Client ===");
        Console.WriteLine("This client connects to the ChatServer using SignalR with PostgreSQL backplane.");
        Console.WriteLine();

        // Get server URL
        Console.Write("Enter server URL (default: http://localhost:5000): ");
        var serverUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            serverUrl = "http://localhost:5000";
        }

        // Get username
        Console.Write("Enter your username: ");
        _userName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(_userName))
        {
            _userName = $"User_{Random.Shared.Next(1000, 9999)}";
        }

        try
        {
            await ConnectToHub(serverUrl);
            await RunClient();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            if (_connection != null)
            {
                await _connection.DisposeAsync();
            }
        }
    }

    private static async Task ConnectToHub(string serverUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/chathub")
            .ConfigureLogging(logging =>
            {
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .WithAutomaticReconnect()
            .Build();

        // Set up event handlers
        _connection.On<string, string>("ReceiveMessage", (user, message) =>
        {
            Console.WriteLine($"[ALL] {user}: {message}");
        });

        _connection.On<string, string, string>("ReceiveGroupMessage", (group, user, message) =>
        {
            Console.WriteLine($"[{group}] {user}: {message}");
        });

        _connection.On<string>("UserConnected", (connectionId) =>
        {
            Console.WriteLine($"[SYSTEM] User connected: {connectionId}");
        });

        _connection.On<string>("UserDisconnected", (connectionId) =>
        {
            Console.WriteLine($"[SYSTEM] User disconnected: {connectionId}");
        });

        _connection.On<string, string>("UserJoinedGroup", (user, group) =>
        {
            if (!string.IsNullOrEmpty(user))
            {
                Console.WriteLine($"[SYSTEM] {user} joined group: {group}");
            }
        });

        _connection.On<string, string>("UserLeftGroup", (user, group) =>
        {
            if (!string.IsNullOrEmpty(user))
            {
                Console.WriteLine($"[SYSTEM] {user} left group: {group}");
            }
        });

        _connection.Reconnecting += (error) =>
        {
            Console.WriteLine($"[SYSTEM] Reconnecting... {error?.Message}");
            return Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) =>
        {
            Console.WriteLine($"[SYSTEM] Reconnected with ID: {connectionId}");
            return Task.CompletedTask;
        };

        _connection.Closed += (error) =>
        {
            Console.WriteLine($"[SYSTEM] Connection closed. {error?.Message}");
            return Task.CompletedTask;
        };

        Console.WriteLine("Connecting to SignalR hub...");
        await _connection.StartAsync();
        Console.WriteLine($"Connected! Connection ID: {_connection.ConnectionId}");
        Console.WriteLine();
    }

    private static async Task RunClient()
    {
        ShowHelp();

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input))
                continue;

            try
            {
                await ProcessCommand(input);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing command: {ex.Message}");
            }
        }
    }

    private static async Task ProcessCommand(string input)
    {
        if (_connection == null || _connection.State != HubConnectionState.Connected)
        {
            Console.WriteLine("Not connected to hub");
            return;
        }

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();

        switch (command)
        {
            case "/help":
            case "/h":
                ShowHelp();
                break;

            case "/join":
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: /join <group_name>");
                    return;
                }
                await _connection.InvokeAsync("JoinGroup", parts[1]);
                Console.WriteLine($"Joined group: {parts[1]}");
                break;

            case "/leave":
                if (parts.Length < 2)
                {
                    Console.WriteLine("Usage: /leave <group_name>");
                    return;
                }
                await _connection.InvokeAsync("LeaveGroup", parts[1]);
                Console.WriteLine($"Left group: {parts[1]}");
                break;

            case "/group":
                if (parts.Length < 3)
                {
                    Console.WriteLine("Usage: /group <group_name> <message>");
                    return;
                }
                var groupName = parts[1];
                var groupMessage = string.Join(" ", parts.Skip(2));
                await _connection.InvokeAsync("SendMessageToGroup", groupName, _userName, groupMessage);
                break;

            case "/quit":
            case "/exit":
                _cancellationTokenSource.Cancel();
                break;

            default:
                // Send as regular message to all
                await _connection.InvokeAsync("SendMessage", _userName, input);
                break;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("=== Available Commands ===");
        Console.WriteLine("/help, /h          - Show this help");
        Console.WriteLine("/join <group>      - Join a group");
        Console.WriteLine("/leave <group>     - Leave a group");
        Console.WriteLine("/group <group> <msg> - Send message to specific group");
        Console.WriteLine("/quit, /exit       - Exit the application");
        Console.WriteLine("Any other text     - Send message to all users");
        Console.WriteLine();
    }
}
