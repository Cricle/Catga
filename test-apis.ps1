# OrderSystem API Testing Script

$baseUrl = "http://localhost:5000"
$passed = 0
$failed = 0

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "   OrderSystem API & UI Tests" -ForegroundColor Cyan
Write-Host "======================================`n" -ForegroundColor Cyan

# Wait for service
Write-Host "Waiting 30 seconds for service..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# Test 1: Health
Write-Host "`n[1/8] Health Check" -ForegroundColor Green
try {
    $h = Invoke-WebRequest -Uri "$baseUrl/health" -Method GET -UseBasicParsing
    Write-Host "OK - Status: $($h.StatusCode)" -ForegroundColor Green
    $passed++
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Test 2: Demo Success
Write-Host "`n[2/8] Demo Success Order" -ForegroundColor Green
try {
    $r = Invoke-RestMethod -Uri "$baseUrl/demo/order-success" -Method POST
    Write-Host "OK" -ForegroundColor Green
    $r | ConvertTo-Json -Depth 3
    $passed++
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Test 3: Demo Failure
Write-Host "`n[3/8] Demo Failure Order (Rollback)" -ForegroundColor Green
try {
    $r = Invoke-RestMethod -Uri "$baseUrl/demo/order-failure" -Method POST
    Write-Host "OK" -ForegroundColor Green
    $r | ConvertTo-Json -Depth 3
    $passed++
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Test 4: Demo Compare
Write-Host "`n[4/8] Demo Compare Info" -ForegroundColor Green
try {
    $r = Invoke-RestMethod -Uri "$baseUrl/demo/compare" -Method GET
    Write-Host "OK" -ForegroundColor Green
    $passed++
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Test 5: Create Order
Write-Host "`n[5/8] Create Order API" -ForegroundColor Green
try {
    $body = @{
        customerId = "TEST-001"
        items = @(@{
            productId = "P1"
            productName = "Product"
            quantity = 1
            unitPrice = 100
        })
        shippingAddress = "Test Addr"
        paymentMethod = "Alipay"
    } | ConvertTo-Json

    $r = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method POST -Body $body -ContentType "application/json"
    Write-Host "OK - OrderId: $($r.orderId)" -ForegroundColor Green
    $global:orderId = $r.orderId
    $passed++
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Test 6: Get Order
Write-Host "`n[6/8] Get Order" -ForegroundColor Green
if ($global:orderId) {
    try {
        $r = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($global:orderId)" -Method GET
        Write-Host "OK" -ForegroundColor Green
        $r | ConvertTo-Json -Depth 3
        $passed++
    } catch {
        Write-Host "FAIL: $_" -ForegroundColor Red
        $failed++
    }
} else {
    Write-Host "SKIP - No OrderId" -ForegroundColor Yellow
}

# Test 7: UI
Write-Host "`n[7/8] UI Homepage" -ForegroundColor Green
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/" -Method GET -UseBasicParsing
    if ($r.StatusCode -eq 200) {
        Write-Host "OK" -ForegroundColor Green
        $passed++
    }
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Test 8: Swagger
Write-Host "`n[8/8] Swagger UI" -ForegroundColor Green
try {
    $r = Invoke-WebRequest -Uri "$baseUrl/swagger/index.html" -Method GET -UseBasicParsing
    if ($r.StatusCode -eq 200) {
        Write-Host "OK" -ForegroundColor Green
        $passed++
    }
} catch {
    Write-Host "FAIL: $_" -ForegroundColor Red
    $failed++
}

# Summary
Write-Host "`n======================================"  -ForegroundColor Cyan
Write-Host "Passed: $passed | Failed: $failed" -ForegroundColor Cyan
Write-Host "======================================`n" -ForegroundColor Cyan

if ($failed -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
    Write-Host "`nAccess Points:" -ForegroundColor Cyan
    Write-Host "  UI: http://localhost:5000"
    Write-Host "  Swagger: http://localhost:5000/swagger"
    Write-Host "  Aspire: http://localhost:15888`n"
}

