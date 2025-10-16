# OrderSystem API 测试脚本

$baseUrl = "http://localhost:5000"
$passed = 0
$failed = 0

Write-Host "`n╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     OrderSystem API & UI 全面测试                        ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# 等待服务启动
Write-Host "⏳ Waiting for service to start (30 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# 测试 1: Health Check
Write-Host "`n[Test 1/8] Health Check Endpoint" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $health = Invoke-WebRequest -Uri "$baseUrl/health" -Method GET -UseBasicParsing
    Write-Host "✅ PASSED - Status: $($health.StatusCode)" -ForegroundColor Green
    $passed++
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试 2: Demo Order Success
Write-Host "`n[Test 2/8] Demo Order Success (POST /demo/order-success)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/demo/order-success" -Method POST -UseBasicParsing
    Write-Host "✅ PASSED" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor White
    $passed++
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试 3: Demo Order Failure (Rollback)
Write-Host "`n[Test 3/8] Demo Order Failure with Rollback (POST /demo/order-failure)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/demo/order-failure" -Method POST -UseBasicParsing
    Write-Host "✅ PASSED" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor White
    $passed++
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试 4: Demo Comparison Info
Write-Host "`n[Test 4/8] Demo Comparison Info (GET /demo/compare)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $response = Invoke-RestMethod -Uri "$baseUrl/demo/compare" -Method GET -UseBasicParsing
    Write-Host "✅ PASSED" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor White
    $passed++
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试 5: Create Order API
Write-Host "`n[Test 5/8] Create Order (POST /api/orders)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $body = @{
        customerId = "API-TEST-001"
        items = @(
            @{
                productId = "PROD-001"
                productName = "Test Product"
                quantity = 2
                unitPrice = 99.99
            }
        )
        shippingAddress = "123 Test Street, Beijing"
        paymentMethod = "Alipay"
    } | ConvertTo-Json

    $response = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method POST -Body $body -ContentType "application/json" -UseBasicParsing
    Write-Host "✅ PASSED" -ForegroundColor Green
    Write-Host "Response:" -ForegroundColor Cyan
    $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor White
    $global:testOrderId = $response.orderId
    $passed++
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试 6: Get Order
Write-Host "`n[Test 6/8] Get Order (GET /api/orders/{orderId})" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
if ($global:testOrderId) {
    try {
        $response = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($global:testOrderId)" -Method GET -UseBasicParsing
        Write-Host "✅ PASSED" -ForegroundColor Green
        Write-Host "Response:" -ForegroundColor Cyan
        $response | ConvertTo-Json -Depth 5 | Write-Host -ForegroundColor White
        $passed++
    } catch {
        Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
        $failed++
    }
} else {
    Write-Host "⏭️ SKIPPED - No order ID from previous test" -ForegroundColor Yellow
}

# 测试 7: UI 页面
Write-Host "`n[Test 7/8] UI Homepage (GET /)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/" -Method GET -UseBasicParsing
    if ($response.StatusCode -eq 200 -and $response.Content -match "OrderSystem") {
        Write-Host "✅ PASSED - UI page loaded successfully" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "❌ FAILED - UI page content unexpected" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试 8: Swagger UI
Write-Host "`n[Test 8/8] Swagger UI (GET /swagger)" -ForegroundColor Green
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor DarkGray
try {
    $response = Invoke-WebRequest -Uri "$baseUrl/swagger/index.html" -Method GET -UseBasicParsing
    if ($response.StatusCode -eq 200) {
        Write-Host "✅ PASSED - Swagger UI accessible" -ForegroundColor Green
        $passed++
    } else {
        Write-Host "❌ FAILED - Swagger UI not accessible" -ForegroundColor Red
        $failed++
    }
} catch {
    Write-Host "❌ FAILED - Error: $_" -ForegroundColor Red
    $failed++
}

# 测试总结
Write-Host "`n╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                    测试总结                               ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "`n✅ Passed: $passed" -ForegroundColor Green
Write-Host "❌ Failed: $failed" -ForegroundColor Red
Write-Host "📊 Total:  $($passed + $failed)`n" -ForegroundColor Cyan

if ($failed -eq 0) {
    Write-Host "🎉 所有测试通过！" -ForegroundColor Green
    Write-Host "`n📊 Access Points:" -ForegroundColor Cyan
    Write-Host "   • UI: http://localhost:5000" -ForegroundColor White
    Write-Host "   • Swagger: http://localhost:5000/swagger" -ForegroundColor White
    Write-Host "   • Aspire Dashboard: http://localhost:15888`n" -ForegroundColor White
} else {
    Write-Host "⚠️ 有 $failed 个测试失败，请检查日志" -ForegroundColor Yellow
}

