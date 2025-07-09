# SignalR PostgreSQL Backplane Example

This example demonstrates how to use SignalR with the PostgreSQL backplane for real-time communication across multiple server instances.

## Projects

### ChatServer
A SignalR server that hosts a chat hub with PostgreSQL backplane for scale-out support.

### ChatClient  
A console application that connects to the ChatServer and allows real-time messaging.

## Prerequisites

1. **PostgreSQL Database**: You need a running PostgreSQL instance
2. **.NET 8.0 SDK**: Required to build and run the projects

## Database Setup

1. Create a PostgreSQL database for the demo:
```sql
CREATE DATABASE signalr_demo;
```

2. Update the connection string in `ChatServer/appsettings.json` if needed:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=signalr_demo;Username=postgres;Password=postgres"
  }
}
```

The PostgreSQL backplane will automatically create the required tables when the server starts.

## Running the Example

### 1. Start the ChatServer

```bash
cd examples/ChatServer
dotnet run
```

The server will start on `http://localhost:5000` by default.

### 2. Start one or more ChatClients

In separate terminals:

```bash
cd examples/ChatClient
dotnet run
```

Each client will prompt you for:
- Server URL (default: http://localhost:5000)
- Your username

## Features Demonstrated

### Basic Messaging
- Send messages to all connected users
- Real-time message delivery across all clients

### Groups
- Join/leave chat groups
- Send messages to specific groups
- Group membership notifications

### Scale-Out with PostgreSQL
- Multiple server instances can be run simultaneously
- Messages are distributed across all servers via PostgreSQL
- Automatic reconnection handling

## Chat Commands

Once connected, you can use these commands in the client:

- `/help` - Show available commands
- `/join <group_name>` - Join a chat group
- `/leave <group_name>` - Leave a chat group  
- `/group <group_name> <message>` - Send message to specific group
- `/quit` - Exit the client
- Any other text - Send message to all users

## Testing Scale-Out

To test the PostgreSQL backplane scale-out functionality:

1. Start multiple ChatServer instances on different ports:
```bash
# Terminal 1
cd examples/ChatServer
dotnet run --urls "http://localhost:5000"

# Terminal 2  
cd examples/ChatServer
dotnet run --urls "http://localhost:5001"
```

2. Connect clients to different server instances:
```bash
# Client connecting to server 1
cd examples/ChatClient
dotnet run
# Enter: http://localhost:5000

# Client connecting to server 2
cd examples/ChatClient  
dotnet run
# Enter: http://localhost:5001
```

3. Messages sent from clients connected to different servers should be visible to all clients, demonstrating the PostgreSQL backplane working correctly.

## PostgreSQL Backplane Features

This example showcases:

- **LISTEN/NOTIFY**: PostgreSQL's LISTEN/NOTIFY feature for real-time message distribution
- **Automatic Table Creation**: The backplane automatically creates required tables
- **Group Management**: Distributed group membership across server instances  
- **Connection Resilience**: Automatic reconnection and error handling
- **Message Serialization**: Efficient message serialization using the PostgreSQL protocol

## Monitoring

The server logs will show:
- PostgreSQL connection status
- Message publishing/receiving
- Group membership changes
- Client connections/disconnections

Set logging to `Debug` or `Trace` level in `appsettings.Development.json` to see detailed PostgreSQL backplane operations.

## Configuration Options

The PostgreSQL backplane can be configured with various options in `Program.cs`:

```csharp
builder.Services.AddSignalR()
    .AddNpgsql(connectionString, options =>
    {
        options.SchemaName = "signalr";  // Database schema for SignalR tables
        options.TableSlugGenerator = hubType => hubType.Name.ToLowerInvariant();
    });
```

## Troubleshooting

### Connection Issues
- Verify PostgreSQL is running and accessible
- Check connection string in `appsettings.json`
- Ensure database exists and user has proper permissions

### Scale-Out Not Working
- Verify all server instances use the same PostgreSQL database
- Check server logs for PostgreSQL backplane errors
- Ensure PostgreSQL supports LISTEN/NOTIFY (most versions do)

### Performance
- Monitor PostgreSQL performance under load
- Consider connection pooling for high-throughput scenarios
- Review PostgreSQL configuration for optimal performance

## Debugging Issues

If messages are not being delivered between clients, try these debugging steps:

### 1. Check PostgreSQL Connection
```bash
./debug-postgres.sh
```

This script will verify:
- PostgreSQL is running and accessible
- SignalR schema and tables were created
- Notification triggers are in place
- Manual notification testing

### 2. Check Server Logs
Look for these log messages when the server starts:
```
[Init] Initializing PostgreSQL backplane with notification channel: SignalRNotificationChannel
[Init] Creating schema 'signalr' and table 'chathub_Messages'
[Init] Database schema and triggers created successfully
[Init] Starting background notification listener and processor tasks
[Listener] Connection opened. Sending LISTEN command...
[Listener] Successfully listening on channel 'SignalRNotificationChannel'. Waiting for notifications...
```

### 3. Check Message Publishing
When sending messages, you should see:
```
[Publish] Publishing message of type InvocationAll
[Publish] Executing SQL: INSERT INTO "signalr"."chathub_Messages" ("Payload") VALUES (@payload)
[Publish] Message inserted successfully. Rows affected: 1
```

### 4. Check Notification Processing
When notifications are received, you should see:
```
[Listener] Received notification on channel 'SignalRNotificationChannel' from PID 12345. Payload: {"Id":1,"Payload":"base64data","InsertedOn":"2025-07-09T..."}
[Processor] Processing incoming message: Id=1
```

### 5. Manual Database Testing
Connect to PostgreSQL directly and test notifications:
```sql
-- Terminal 1: Listen for notifications
docker exec -it signalr-postgres-demo psql -U postgres -d signalr_demo
LISTEN SignalRNotificationChannel;

-- Terminal 2: Send a test notification
docker exec signalr-postgres-demo psql -U postgres -d signalr_demo -c "SELECT pg_notify('SignalRNotificationChannel', 'test');"
```

### 6. Common Issues

**No notifications received:**
- Check if PostgreSQL LISTEN/NOTIFY is supported in your PostgreSQL version
- Verify the notification channel name matches between sender and listener
- Ensure the trigger was created successfully

**Database connection errors:**
- Verify the connection string in `appsettings.json`
- Check if the database exists and is accessible
- Ensure the user has proper permissions to create schemas and tables

**Messages not reaching other server instances:**
- Verify all servers use the same PostgreSQL database
- Check that all servers are successfully listening on the same notification channel
- Look for any errors in the background listener tasks
