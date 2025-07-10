#!/bin/bash

# Start multiple ChatServer instances for testing scale-out

echo "Starting multiple ChatServer instances for PostgreSQL backplane testing..."
echo "Press Ctrl+C to stop all servers"

# Function to cleanup background processes
cleanup() {
    echo -e "\nStopping all servers..."
    jobs -p | xargs -r kill
    exit 0
}

# Set up trap for cleanup
trap cleanup SIGINT SIGTERM

# Start first server on port 5000
echo "Starting ChatServer on port 5000..."
cd ChatServer
dotnet run --urls "http://localhost:5000" &
SERVER1_PID=$!

# Wait a moment for first server to start
sleep 3

# Start second server on port 5001
echo "Starting ChatServer on port 5001..."
dotnet run --urls "http://localhost:5001" &
SERVER2_PID=$!

# Wait a moment for second server to start
sleep 3

# Start third server on port 5002
echo "Starting ChatServer on port 5002..."
dotnet run --urls "http://localhost:5002" &
SERVER3_PID=$!

echo ""
echo "✅ All servers started!"
echo "📡 Server 1: http://localhost:5000/chathub"
echo "📡 Server 2: http://localhost:5001/chathub" 
echo "📡 Server 3: http://localhost:5002/chathub"
echo ""
echo "Now start ChatClient instances and connect to different servers to test scale-out."
echo "Press Ctrl+C to stop all servers."

# Wait for all background jobs
wait
