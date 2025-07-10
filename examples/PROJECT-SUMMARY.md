# SignalR PostgreSQL Backplane - Server and Client Projects

I've created two new projects that demonstrate SignalR with the PostgreSQL backplane:

## Projects Created

### 📡 ChatServer (`examples/ChatServer/`)
A SignalR server application that demonstrates:
- **ChatHub**: A SignalR Hub with messaging, group management, and connection tracking
- **PostgreSQL Backplane Integration**: Uses `AspNetCore.SignalR.Npgsql` for scale-out messaging
- **CORS Configuration**: Allows cross-origin connections
- **Health Endpoints**: Simple endpoints for monitoring server status
- **Comprehensive Logging**: Detailed logging for debugging and monitoring

**Key Features:**
- Send messages to all connected users
- Join/leave chat groups with notifications
- Send messages to specific groups
- Real-time connection/disconnection notifications
- Scale-out support via PostgreSQL LISTEN/NOTIFY

### 💬 ChatClient (`examples/ChatClient/`)
A console-based SignalR client application that demonstrates:
- **SignalR Client Connection**: Connects to ChatServer with automatic reconnection
- **Interactive Commands**: Command-line interface for testing all server features
- **Real-time Message Handling**: Receives and displays messages from different sources
- **Group Management**: Join/leave groups and send group-specific messages
- **Connection Resilience**: Handles disconnections and reconnections gracefully

**Available Commands:**
- `/help` - Show available commands
- `/join <group>` - Join a chat group
- `/leave <group>` - Leave a chat group
- `/group <group> <message>` - Send message to specific group
- `/quit` - Exit the application
- Any other text - Send message to all users

## PostgreSQL Backplane Features Demonstrated

### 🔄 Scale-Out Messaging
- Multiple server instances share messages via PostgreSQL
- LISTEN/NOTIFY for real-time message distribution
- Automatic table creation and schema management

### 👥 Distributed Group Management
- Group membership synchronized across server instances
- Group join/leave operations distributed to all servers
- Messages sent to groups reach all members regardless of server

### 🔌 Connection Management
- Connection events distributed across all servers
- Automatic cleanup of disconnected users
- Resilient reconnection handling

## Quick Start

### 1. Start PostgreSQL
```bash
cd examples
docker-compose up -d
```

### 2. Start the Server
```bash
cd examples/ChatServer
dotnet run
```

### 3. Start Client(s)
```bash
cd examples/ChatClient
dotnet run
```

### 4. Test Scale-Out (Optional)
```bash
cd examples
./start-multiple-servers.sh    # Linux/Mac
# or
./start-multiple-servers.ps1   # Windows PowerShell
```

## Configuration

### Server Configuration (`appsettings.json`)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=signalr_demo;Username=postgres;Password=postgres"
  },
  "Logging": {
    "LogLevel": {
      "AspNetCore.SignalR.Npgsql": "Debug"
    }
  }
}
```

### PostgreSQL Backplane Options
```csharp
builder.Services.AddSignalR()
    .AddNpgsql(connectionString, options =>
    {
        options.SchemaName = "signalr";
        options.TableSlugGenerator = hubType => hubType.Name.ToLowerInvariant();
    });
```

## Testing Scenarios

### ✅ Basic Messaging
1. Start server and multiple clients
2. Send messages from any client
3. Verify all clients receive messages

### ✅ Group Management
1. Use `/join groupname` to join groups
2. Use `/group groupname message` to send group messages
3. Verify only group members receive group messages

### ✅ Scale-Out Testing
1. Start multiple server instances on different ports
2. Connect clients to different servers
3. Send messages from clients on different servers
4. Verify all clients receive messages regardless of server

### ✅ Connection Resilience
1. Start server and client
2. Stop server temporarily
3. Restart server
4. Verify client automatically reconnects

## Files Created

```
examples/
├── README.md                          # Comprehensive documentation
├── docker-compose.yml                 # PostgreSQL setup
├── start-multiple-servers.sh          # Linux/Mac multi-server script
├── start-multiple-servers.ps1         # Windows PowerShell script
├── ChatServer/
│   ├── ChatServer.csproj              # Server project file
│   ├── Program.cs                     # Server startup and configuration
│   ├── appsettings.json               # Production configuration
│   ├── appsettings.Development.json   # Development configuration
│   └── Hubs/
│       └── ChatHub.cs                 # SignalR Hub implementation
└── ChatClient/
    ├── ChatClient.csproj              # Client project file
    └── Program.cs                     # Interactive console client
```

## Additional Tools Created

### 🛠️ VS Code Tasks (`.vscode/tasks.json`)
- `build-chat-server` - Build the server
- `run-chat-server` - Run the server
- `build-chat-client` - Build the client  
- `run-chat-client` - Run the client
- `start-postgres` - Start PostgreSQL via Docker
- `stop-postgres` - Stop PostgreSQL

### 🚀 Multi-Server Scripts
- **Bash script** (`start-multiple-servers.sh`) for Linux/Mac
- **PowerShell script** (`start-multiple-servers.ps1`) for Windows
- Both start 3 server instances on ports 5000, 5001, 5002

## Next Steps

You can now:
1. **Test the basic functionality** by running the server and client
2. **Experiment with scale-out** using the multi-server scripts
3. **Modify the ChatHub** to add more features
4. **Extend the client** with additional commands or GUI
5. **Monitor PostgreSQL** to see the backplane in action
6. **Load test** with multiple clients and servers

The projects demonstrate all the key aspects of using SignalR with a PostgreSQL backplane for real-time, scalable communication across multiple server instances.
