#!/bin/bash

echo "=== PostgreSQL SignalR Backplane Debug Script ==="
echo ""

# Check if PostgreSQL is running
echo "1. Checking PostgreSQL connection..."
if docker exec signalr-postgres-demo pg_isready -U postgres -d signalr_demo > /dev/null 2>&1; then
    echo "✅ PostgreSQL is running"
else
    echo "❌ PostgreSQL is not running or not accessible"
    echo "Run 'docker-compose up -d' to start PostgreSQL"
    exit 1
fi

echo ""
echo "2. Checking if SignalR schema and tables exist..."

# Check if the schema and tables exist
SCHEMA_CHECK=$(docker exec signalr-postgres-demo psql -U postgres -d signalr_demo -t -c "SELECT EXISTS(SELECT 1 FROM information_schema.schemata WHERE schema_name = 'signalr');" 2>/dev/null | tr -d ' ')

if [ "$SCHEMA_CHECK" = "t" ]; then
    echo "✅ SignalR schema exists"
    
    # Check for tables
    TABLE_CHECK=$(docker exec signalr-postgres-demo psql -U postgres -d signalr_demo -t -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'signalr' AND table_name LIKE '%_Messages';" 2>/dev/null | tr -d ' ')
    
    if [ "$TABLE_CHECK" -gt "0" ]; then
        echo "✅ Found $TABLE_CHECK SignalR message table(s)"
        
        # Show the tables
        echo ""
        echo "3. SignalR tables found:"
        docker exec signalr-postgres-demo psql -U postgres -d signalr_demo -c "SELECT table_name FROM information_schema.tables WHERE table_schema = 'signalr' AND table_name LIKE '%_Messages';"
        
        echo ""
        echo "4. Checking for triggers and functions..."
        TRIGGER_CHECK=$(docker exec signalr-postgres-demo psql -U postgres -d signalr_demo -t -c "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema = 'signalr' AND trigger_name LIKE '%_insert_trigger';" 2>/dev/null | tr -d ' ')
        
        if [ "$TRIGGER_CHECK" -gt "0" ]; then
            echo "✅ Found $TRIGGER_CHECK notification trigger(s)"
        else
            echo "❌ No notification triggers found - this could be the problem!"
        fi
        
        echo ""
        echo "5. Testing manual notification..."
        echo "Sending test notification..."
        docker exec signalr-postgres-demo psql -U postgres -d signalr_demo -c "SELECT pg_notify('SignalRNotificationChannel', 'test message');"
        
    else
        echo "❌ No SignalR message tables found"
        echo "The server may not have initialized properly"
    fi
else
    echo "❌ SignalR schema does not exist"
    echo "The server has not initialized the database yet"
fi

echo ""
echo "6. Database connection info:"
echo "Host: localhost:5432"
echo "Database: signalr_demo"
echo "Username: postgres"
echo "Password: postgres"

echo ""
echo "To manually test notifications:"
echo "1. Connect to PostgreSQL: docker exec -it signalr-postgres-demo psql -U postgres -d signalr_demo"
echo "2. Run: LISTEN SignalRNotificationChannel;"
echo "3. In another terminal, run the ChatServer"
echo "4. Send a message from a client"
echo "5. You should see notifications in the first terminal"
