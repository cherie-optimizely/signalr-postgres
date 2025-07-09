# Start multiple ChatServer instances for testing PostgreSQL backplane scale-out

Write-Host "Starting multiple ChatServer instances for PostgreSQL backplane testing..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop all servers" -ForegroundColor Yellow

# Start first server on port 5000
Write-Host "Starting ChatServer on port 5000..." -ForegroundColor Cyan
Set-Location ChatServer
$server1 = Start-Process -FilePath "dotnet" -ArgumentList "run --urls http://localhost:5000" -PassThru -NoNewWindow

# Wait a moment for first server to start
Start-Sleep -Seconds 3

# Start second server on port 5001
Write-Host "Starting ChatServer on port 5001..." -ForegroundColor Cyan
$server2 = Start-Process -FilePath "dotnet" -ArgumentList "run --urls http://localhost:5001" -PassThru -NoNewWindow

# Wait a moment for second server to start
Start-Sleep -Seconds 3

# Start third server on port 5002
Write-Host "Starting ChatServer on port 5002..." -ForegroundColor Cyan  
$server3 = Start-Process -FilePath "dotnet" -ArgumentList "run --urls http://localhost:5002" -PassThru -NoNewWindow

Write-Host ""
Write-Host "✅ All servers started!" -ForegroundColor Green
Write-Host "📡 Server 1: http://localhost:5000/chathub" -ForegroundColor White
Write-Host "📡 Server 2: http://localhost:5001/chathub" -ForegroundColor White
Write-Host "📡 Server 3: http://localhost:5002/chathub" -ForegroundColor White
Write-Host ""
Write-Host "Now start ChatClient instances and connect to different servers to test scale-out." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop all servers." -ForegroundColor Yellow

# Function to cleanup
function Stop-Servers {
    Write-Host "`nStopping all servers..." -ForegroundColor Red
    if (!$server1.HasExited) { $server1.Kill() }
    if (!$server2.HasExited) { $server2.Kill() }
    if (!$server3.HasExited) { $server3.Kill() }
    exit 0
}

# Set up Ctrl+C handler
$null = [Console]::TreatControlCAsInput = $false
[Console]::CancelKeyPress += {
    Stop-Servers
}

# Wait for user to press Ctrl+C
try {
    while ($true) {
        Start-Sleep -Seconds 1
        
        # Check if any server has exited
        if ($server1.HasExited -or $server2.HasExited -or $server3.HasExited) {
            Write-Host "One or more servers have stopped. Stopping all..." -ForegroundColor Red
            Stop-Servers
        }
    }
}
finally {
    Stop-Servers
}
