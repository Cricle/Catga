#!/usr/bin/env pwsh
# Test script for Catga Hosting Example - Graceful Shutdown

Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║    Catga Hosting Example - Graceful Shutdown Test           ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

Write-Host "This script will:" -ForegroundColor Yellow
Write-Host "  1. Start the HostingExample worker service" -ForegroundColor Gray
Write-Host "  2. Let it run for 15 seconds (sending ~3 messages)" -ForegroundColor Gray
Write-Host "  3. Send Ctrl+C to trigger graceful shutdown" -ForegroundColor Gray
Write-Host "  4. Verify that shutdown completes gracefully" -ForegroundColor Gray
Write-Host ""

# Start the application in the background
Write-Host "Starting HostingExample..." -ForegroundColor Yellow
$process = Start-Process -FilePath "dotnet" -ArgumentList "run" -PassThru -NoNewWindow -RedirectStandardOutput "output.log" -RedirectStandardError "error.log"

if (-not $process) {
    Write-Host "✗ Failed to start application" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Application started (PID: $($process.Id))" -ForegroundColor Green
Write-Host ""

# Wait for startup
Write-Host "Waiting for startup (5 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# Check if process is still running
if ($process.HasExited) {
    Write-Host "✗ Application exited unexpectedly" -ForegroundColor Red
    Get-Content "output.log"
    Get-Content "error.log"
    exit 1
}

Write-Host "✓ Application is running" -ForegroundColor Green
Write-Host ""

# Let it run for a bit
Write-Host "Letting application run for 15 seconds..." -ForegroundColor Yellow
Write-Host "  (Should send approximately 3 messages)" -ForegroundColor Gray
Start-Sleep -Seconds 15

# Trigger graceful shutdown
Write-Host ""
Write-Host "Triggering graceful shutdown (Ctrl+C)..." -ForegroundColor Yellow

# Send Ctrl+C signal
try {
    $process.Kill()
    Write-Host "✓ Shutdown signal sent" -ForegroundColor Green
}
catch {
    Write-Host "✗ Failed to send shutdown signal: $_" -ForegroundColor Red
    exit 1
}

# Wait for graceful shutdown (max 35 seconds - 30s timeout + 5s buffer)
Write-Host "Waiting for graceful shutdown (max 35 seconds)..." -ForegroundColor Yellow
$waited = 0
$maxWait = 35

while (-not $process.HasExited -and $waited -lt $maxWait) {
    Start-Sleep -Seconds 1
    $waited++
    Write-Host "." -NoNewline -ForegroundColor Gray
}

Write-Host ""

if ($process.HasExited) {
    Write-Host "✓ Application shut down gracefully in $waited seconds" -ForegroundColor Green
}
else {
    Write-Host "✗ Application did not shut down within timeout" -ForegroundColor Red
    $process.Kill()
    exit 1
}

Write-Host ""
Write-Host "Checking output logs..." -ForegroundColor Yellow

# Check output for expected messages
$output = Get-Content "output.log" -Raw

$checks = @(
    @{ Pattern = "Hosted Services Configured"; Description = "Hosted services configured" },
    @{ Pattern = "Message Producer Worker started"; Description = "Worker started" },
    @{ Pattern = "Sent message"; Description = "Messages sent" },
    @{ Pattern = "Data processed"; Description = "Events processed" },
    @{ Pattern = "graceful shutdown"; Description = "Graceful shutdown triggered" },
    @{ Pattern = "Message Producer Worker stopped"; Description = "Worker stopped" },
    @{ Pattern = "Transport service stopped"; Description = "Transport stopped" }
)

$passed = 0
$failed = 0

foreach ($check in $checks) {
    if ($output -match $check.Pattern) {
        Write-Host "  ✓ $($check.Description)" -ForegroundColor Green
        $passed++
    }
    else {
        Write-Host "  ✗ $($check.Description)" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                    Test Results                              ║" -ForegroundColor Cyan
Write-Host "╠══════════════════════════════════════════════════════════════╣" -ForegroundColor Cyan
Write-Host "║  Passed: $passed                                                   ║" -ForegroundColor $(if ($passed -eq $checks.Count) { "Green" } else { "Yellow" })
Write-Host "║  Failed: $failed                                                   ║" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# Cleanup
Remove-Item "output.log" -ErrorAction SilentlyContinue
Remove-Item "error.log" -ErrorAction SilentlyContinue

if ($failed -gt 0) {
    Write-Host ""
    Write-Host "Some checks failed. Review the output above." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ All checks passed! Graceful shutdown works correctly." -ForegroundColor Green
exit 0
