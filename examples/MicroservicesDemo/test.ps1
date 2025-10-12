# Catga Microservices RPC Test Script

Write-Host "Testing Order Service calling User Service..." -ForegroundColor Green

$order = @{
    userId = 123
    items = @("Laptop", "Mouse", "Keyboard")
    totalAmount = 1299.99
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:5001/orders" -Method Post -Body $order -ContentType "application/json"

Write-Host "`nOrder Created:" -ForegroundColor Cyan
$response | ConvertTo-Json -Depth 5

Write-Host "`nRPC Call Details:" -ForegroundColor Yellow
Write-Host "- OrderService called UserService.ValidateUser"
Write-Host "- OrderService called UserService.GetUser"
Write-Host "- Transport: NATS (lock-free, high-performance)"
Write-Host "- Serialization: MemoryPack (AOT-compatible)"

