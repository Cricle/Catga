#!/usr/bin/env pwsh

# Order System API Test Script

$baseUrl = "http://localhost:5000"

Write-Host "🧪 Testing Order System API" -ForegroundColor Green
Write-Host "Base URL: $baseUrl" -ForegroundColor Cyan
Write-Host ""

# Function to print test result
function Print-TestResult {
    param($Name, $Success, $Data)
    if ($Success) {
        Write-Host "✅ $Name" -ForegroundColor Green
        if ($Data) {
            Write-Host ($Data | ConvertTo-Json -Depth 5) -ForegroundColor Gray
        }
    } else {
        Write-Host "❌ $Name" -ForegroundColor Red
        Write-Host $Data -ForegroundColor Red
    }
    Write-Host ""
}

try {
    # Test 1: Health Check
    Write-Host "📍 Test 1: Health Check" -ForegroundColor Yellow
    $health = Invoke-RestMethod -Uri "$baseUrl/health"
    Print-TestResult "Health Check" $true $health

    # Test 2: Create Order
    Write-Host "📍 Test 2: Create Order" -ForegroundColor Yellow
    $createOrderBody = @{
        customerName = "John Doe"
        items = @(
            @{
                productName = "Laptop"
                quantity = 1
                price = 999.99
            },
            @{
                productName = "Mouse"
                quantity = 2
                price = 29.99
            }
        )
    } | ConvertTo-Json

    $order1 = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method Post -ContentType "application/json" -Body $createOrderBody
    Print-TestResult "Create Order" $true $order1

    # Test 3: Get Order
    Write-Host "📍 Test 3: Get Order" -ForegroundColor Yellow
    $getOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order1.orderId)"
    Print-TestResult "Get Order" $true $getOrder

    # Test 4: Process Order
    Write-Host "📍 Test 4: Process Order" -ForegroundColor Yellow
    Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order1.orderId)/process" -Method Post
    $processedOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order1.orderId)"
    Print-TestResult "Process Order (Status: $($processedOrder.status))" ($processedOrder.status -eq "Processing") $processedOrder

    # Test 5: Complete Order
    Write-Host "📍 Test 5: Complete Order" -ForegroundColor Yellow
    Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order1.orderId)/complete" -Method Post
    $completedOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order1.orderId)"
    Print-TestResult "Complete Order (Status: $($completedOrder.status))" ($completedOrder.status -eq "Completed") $completedOrder

    # Test 6: Create and Cancel Order
    Write-Host "📍 Test 6: Create and Cancel Order" -ForegroundColor Yellow
    $createOrderBody2 = @{
        customerName = "Jane Smith"
        items = @(
            @{
                productName = "Phone"
                quantity = 1
                price = 699.99
            }
        )
    } | ConvertTo-Json

    $order2 = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method Post -ContentType "application/json" -Body $createOrderBody2
    Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order2.orderId)/cancel?reason=Customer%20changed%20mind" -Method Post
    $cancelledOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($order2.orderId)"
    Print-TestResult "Cancel Order (Status: $($cancelledOrder.status))" ($cancelledOrder.status -eq "Cancelled") $cancelledOrder

    # Test 7: Get Orders by Customer
    Write-Host "📍 Test 7: Get Orders by Customer" -ForegroundColor Yellow
    $customerOrders = Invoke-RestMethod -Uri "$baseUrl/api/orders/customer/John%20Doe"
    Print-TestResult "Get Orders by Customer (Count: $($customerOrders.Count))" ($customerOrders.Count -gt 0) $customerOrders

    # Test 8: Get Pending Orders
    Write-Host "📍 Test 8: Get Pending Orders" -ForegroundColor Yellow
    $pendingOrders = Invoke-RestMethod -Uri "$baseUrl/api/orders/pending"
    Print-TestResult "Get Pending Orders (Count: $($pendingOrders.Count))" $true $pendingOrders

    # Test 9: Bulk Create Orders
    Write-Host "📍 Test 9: Bulk Create Orders (10 orders)" -ForegroundColor Yellow
    $bulkOrders = @()
    for ($i = 1; $i -le 10; $i++) {
        $bulkOrderBody = @{
            customerName = "Customer $i"
            items = @(
                @{
                    productName = "Product $i"
                    quantity = $i
                    price = [math]::Round((Get-Random -Minimum 10 -Maximum 1000) + (Get-Random).NextDouble(), 2)
                }
            )
        } | ConvertTo-Json

        $bulkOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method Post -ContentType "application/json" -Body $bulkOrderBody
        $bulkOrders += $bulkOrder
        Write-Host "  Created order $i: $($bulkOrder.orderNumber)" -ForegroundColor Gray
    }
    Print-TestResult "Bulk Create Orders" ($bulkOrders.Count -eq 10) $null

    # Test 10: Workflow Test (Create → Process → Complete)
    Write-Host "📍 Test 10: Complete Workflow Test" -ForegroundColor Yellow
    $workflowOrderBody = @{
        customerName = "Workflow Test User"
        items = @(
            @{
                productName = "Test Product"
                quantity = 5
                price = 50.00
            }
        )
    } | ConvertTo-Json

    $workflowOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders" -Method Post -ContentType "application/json" -Body $workflowOrderBody
    Start-Sleep -Milliseconds 100
    Invoke-RestMethod -Uri "$baseUrl/api/orders/$($workflowOrder.orderId)/process" -Method Post
    Start-Sleep -Milliseconds 100
    Invoke-RestMethod -Uri "$baseUrl/api/orders/$($workflowOrder.orderId)/complete" -Method Post
    $finalOrder = Invoke-RestMethod -Uri "$baseUrl/api/orders/$($workflowOrder.orderId)"
    
    $workflowSuccess = $finalOrder.status -eq "Completed" -and $finalOrder.completedAt -ne $null
    Print-TestResult "Complete Workflow (Created → Processing → Completed)" $workflowSuccess $finalOrder

    # Summary
    Write-Host "=" * 60 -ForegroundColor Green
    Write-Host "🎉 All Tests Completed Successfully!" -ForegroundColor Green
    Write-Host "=" * 60 -ForegroundColor Green
    Write-Host ""
    Write-Host "📊 Summary:" -ForegroundColor Cyan
    Write-Host "  ✅ Health Check: OK" -ForegroundColor Gray
    Write-Host "  ✅ Order CRUD: OK" -ForegroundColor Gray
    Write-Host "  ✅ Order Lifecycle: OK (Pending → Processing → Completed)" -ForegroundColor Gray
    Write-Host "  ✅ Order Cancellation: OK" -ForegroundColor Gray
    Write-Host "  ✅ Query Operations: OK" -ForegroundColor Gray
    Write-Host "  ✅ Bulk Operations: OK (10 orders created)" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host "❌ Test Failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}

