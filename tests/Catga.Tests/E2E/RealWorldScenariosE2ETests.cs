using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Catga.Abstractions;
using Catga.Core;
using Catga.Flow.Dsl;
using Catga.Flow.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.E2E;

/// <summary>
/// Real-world scenario E2E tests covering complex business workflows.
/// Tests complete end-to-end flows with realistic data and constraints.
/// </summary>
public class RealWorldScenariosE2ETests
{
    private readonly ITestOutputHelper _output;

    public RealWorldScenariosE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region E-Commerce Scenarios

    [Fact]
    public async Task E2E_HighValueOrderProcessing_WithFraudDetection()
    {
        // Arrange - High-value order with fraud detection
        var services = new ServiceCollection();
        var mediator = SetupHighValueOrderMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<HighValueOrderState, HighValueOrderFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<HighValueOrderState, HighValueOrderFlow>>();

        var order = new HighValueOrderState
        {
            FlowId = "high-value-001",
            OrderId = "ORD-HV-001",
            CustomerId = "CUST-PREMIUM-001",
            Amount = 50000.00m,
            Items = new List<OrderItemDetail>
            {
                new() { ProductId = "DIAMOND-001", Name = "Diamond Ring", Quantity = 1, Price = 25000.00m },
                new() { ProductId = "WATCH-001", Name = "Luxury Watch", Quantity = 1, Price = 25000.00m }
            },
            CustomerRiskScore = 0.15m,
            ShippingAddress = new AddressDetail
            {
                Country = "US",
                State = "CA",
                City = "Beverly Hills"
            }
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(order);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.FraudCheckPassed.Should().BeTrue();
        result.State.ManualReviewRequired.Should().BeFalse();
        result.State.PaymentAuthorized.Should().BeTrue();
        result.State.OrderConfirmed.Should().BeTrue();
        result.State.InsuranceApplied.Should().BeTrue();
        result.State.PremiumShippingApplied.Should().BeTrue();

        _output.WriteLine($"✓ High-value order processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Order Amount: ${result.State.Amount:F2}");
        _output.WriteLine($"  Fraud Risk: {result.State.CustomerRiskScore:P}");
        _output.WriteLine($"  Insurance Premium: ${result.State.InsurancePremium:F2}");
    }

    [Fact]
    public async Task E2E_BulkOrderProcessing_WithInventoryAllocation()
    {
        // Arrange - Bulk order with inventory allocation
        var services = new ServiceCollection();
        var mediator = SetupBulkOrderMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<BulkOrderState, BulkOrderFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<BulkOrderState, BulkOrderFlow>>();

        var items = Enumerable.Range(1, 50).Select(i => new BulkOrderItem
        {
            ProductId = $"PROD-{i:D3}",
            Quantity = 100 + i * 10,
            UnitPrice = 10.00m + (i * 0.5m)
        }).ToList();

        var order = new BulkOrderState
        {
            FlowId = "bulk-001",
            OrderId = "ORD-BULK-001",
            CustomerId = "CUST-WHOLESALE-001",
            Items = items,
            OrderType = "Wholesale",
            DiscountPercentage = 0.25m
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(order);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.AllItemsAllocated.Should().BeTrue();
        result.State.AllocatedItems.Should().HaveCount(50);
        result.State.TotalQuantity.Should().Be(items.Sum(i => i.Quantity));
        result.State.DiscountApplied.Should().BeGreaterThan(0);
        result.State.WarehouseAssigned.Should().NotBeNullOrEmpty();
        result.State.ShippingScheduled.Should().BeTrue();

        _output.WriteLine($"✓ Bulk order processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Total Items: {result.State.Items.Count}");
        _output.WriteLine($"  Total Quantity: {result.State.TotalQuantity}");
        _output.WriteLine($"  Discount: {result.State.DiscountPercentage:P}");
        _output.WriteLine($"  Warehouse: {result.State.WarehouseAssigned}");
    }

    [Fact]
    public async Task E2E_InternationalOrderProcessing_WithCustomsAndTax()
    {
        // Arrange - International order with customs and tax
        var services = new ServiceCollection();
        var mediator = SetupInternationalOrderMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<InternationalOrderState, InternationalOrderFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<InternationalOrderState, InternationalOrderFlow>>();

        var order = new InternationalOrderState
        {
            FlowId = "intl-001",
            OrderId = "ORD-INTL-001",
            CustomerId = "CUST-JP-001",
            Amount = 5000.00m,
            DestinationCountry = "JP",
            OriginCountry = "US",
            Items = new List<OrderItemDetail>
            {
                new() { ProductId = "ELECTRONICS-001", Name = "Laptop", Quantity = 1, Price = 2000.00m },
                new() { ProductId = "ACCESSORIES-001", Name = "Accessories", Quantity = 5, Price = 600.00m }
            }
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(order);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CustomsDeclarationCreated.Should().BeTrue();
        result.State.TaxCalculated.Should().BeTrue();
        result.State.TaxAmount.Should().BeGreaterThan(0);
        result.State.DutiesCalculated.Should().BeTrue();
        result.State.DutiesAmount.Should().BeGreaterThan(0);
        result.State.ExportLicenseObtained.Should().BeTrue();
        result.State.ShippingMethodSelected.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✓ International order processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Destination: {result.State.DestinationCountry}");
        _output.WriteLine($"  Order Amount: ${result.State.Amount:F2}");
        _output.WriteLine($"  Tax: ${result.State.TaxAmount:F2}");
        _output.WriteLine($"  Duties: ${result.State.DutiesAmount:F2}");
        _output.WriteLine($"  Total: ${result.State.Amount + result.State.TaxAmount + result.State.DutiesAmount:F2}");
    }

    #endregion

    #region Financial Scenarios

    [Fact]
    public async Task E2E_PaymentProcessing_WithMultiplePaymentMethods()
    {
        // Arrange - Payment with multiple methods
        var services = new ServiceCollection();
        var mediator = SetupPaymentMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<PaymentProcessingState, PaymentProcessingFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<PaymentProcessingState, PaymentProcessingFlow>>();

        var payment = new PaymentProcessingState
        {
            FlowId = "payment-001",
            TransactionId = Guid.NewGuid().ToString(),
            Amount = 1500.00m,
            PaymentMethods = new List<PaymentMethod>
            {
                new() { Type = "CreditCard", Amount = 1000.00m, CardLast4 = "4242" },
                new() { Type = "PayPal", Amount = 500.00m, AccountEmail = "user@example.com" }
            },
            Currency = "USD",
            Merchant = "MERCHANT-001"
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(payment);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.PaymentMethods.Should().AllSatisfy(m => m.Processed.Should().BeTrue());
        result.State.TotalProcessed.Should().Be(1500.00m);
        result.State.AllPaymentsSuccessful.Should().BeTrue();
        result.State.ReceiptGenerated.Should().BeTrue();
        result.State.ConfirmationEmailSent.Should().BeTrue();

        _output.WriteLine($"✓ Multi-method payment processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Total Amount: ${result.State.Amount:F2}");
        _output.WriteLine($"  Payment Methods: {result.State.PaymentMethods.Count}");
        _output.WriteLine($"  Total Processed: ${result.State.TotalProcessed:F2}");
    }

    [Fact]
    public async Task E2E_RefundProcessing_WithPartialRefund()
    {
        // Arrange - Partial refund processing
        var services = new ServiceCollection();
        var mediator = SetupRefundMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<RefundProcessingState, RefundProcessingFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<RefundProcessingState, RefundProcessingFlow>>();

        var refund = new RefundProcessingState
        {
            FlowId = "refund-001",
            OriginalOrderId = "ORD-001",
            OriginalAmount = 1000.00m,
            RefundAmount = 600.00m,
            RefundReason = "PartialReturn",
            Items = new List<RefundItem>
            {
                new() { ProductId = "PROD-001", Quantity = 2, RefundAmount = 400.00m },
                new() { ProductId = "PROD-002", Quantity = 1, RefundAmount = 200.00m }
            }
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(refund);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.RefundApproved.Should().BeTrue();
        result.State.ItemsProcessed.Should().Be(2);
        result.State.RefundInitiated.Should().BeTrue();
        result.State.InventoryRestocked.Should().BeTrue();
        result.State.CustomerNotified.Should().BeTrue();

        _output.WriteLine($"✓ Refund processed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Original Amount: ${result.State.OriginalAmount:F2}");
        _output.WriteLine($"  Refund Amount: ${result.State.RefundAmount:F2}");
        _output.WriteLine($"  Items Returned: {result.State.Items.Count}");
    }

    #endregion

    #region Logistics Scenarios

    [Fact]
    public async Task E2E_ShippingOptimization_WithMultipleCarriers()
    {
        // Arrange - Shipping optimization
        var services = new ServiceCollection();
        var mediator = SetupShippingMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<ShippingOptimizationState, ShippingOptimizationFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<ShippingOptimizationState, ShippingOptimizationFlow>>();

        var shipping = new ShippingOptimizationState
        {
            FlowId = "shipping-001",
            OrderId = "ORD-001",
            Weight = 15.5m,
            Dimensions = new Dimensions { Length = 30, Width = 20, Height = 15 },
            OriginZipCode = "90001",
            DestinationZipCode = "10001",
            DeliveryDeadline = DateTime.UtcNow.AddDays(3),
            ShippingPreference = "CostOptimized"
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(shipping);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.CarrierQuotesReceived.Should().BeGreaterThan(0);
        result.State.OptimalCarrierSelected.Should().NotBeNullOrEmpty();
        result.State.ShippingCost.Should().BeGreaterThan(0);
        result.State.EstimatedDelivery.Should().BeAfter(DateTime.UtcNow);
        result.State.LabelGenerated.Should().BeTrue();
        result.State.TrackingNumber.Should().NotBeNullOrEmpty();

        _output.WriteLine($"✓ Shipping optimized in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Weight: {result.State.Weight}kg");
        _output.WriteLine($"  Carriers Quoted: {result.State.CarrierQuotesReceived}");
        _output.WriteLine($"  Selected Carrier: {result.State.OptimalCarrierSelected}");
        _output.WriteLine($"  Shipping Cost: ${result.State.ShippingCost:F2}");
        _output.WriteLine($"  Tracking: {result.State.TrackingNumber}");
    }

    [Fact]
    public async Task E2E_WarehouseManagement_WithInventorySync()
    {
        // Arrange - Warehouse management
        var services = new ServiceCollection();
        var mediator = SetupWarehouseMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<WarehouseManagementState, WarehouseManagementFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<WarehouseManagementState, WarehouseManagementFlow>>();

        var warehouse = new WarehouseManagementState
        {
            FlowId = "warehouse-001",
            WarehouseId = "WH-001",
            Orders = Enumerable.Range(1, 20).Select(i => new WarehouseOrder
            {
                OrderId = $"ORD-{i:D3}",
                Items = new List<WarehouseOrderItem>
                {
                    new() { ProductId = $"PROD-{i}", Quantity = 10 + i }
                }
            }).ToList()
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(warehouse);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.OrdersProcessed.Should().Be(20);
        result.State.InventorySynced.Should().BeTrue();
        result.State.PickListsGenerated.Should().Be(20);
        result.State.PackingStarted.Should().BeTrue();
        result.State.ShipmentsScheduled.Should().Be(20);

        _output.WriteLine($"✓ Warehouse management completed in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Orders Processed: {result.State.OrdersProcessed}");
        _output.WriteLine($"  Pick Lists: {result.State.PickListsGenerated}");
        _output.WriteLine($"  Shipments Scheduled: {result.State.ShipmentsScheduled}");
    }

    #endregion

    #region Customer Service Scenarios

    [Fact]
    public async Task E2E_CustomerSupportTicket_WithEscalation()
    {
        // Arrange - Customer support with escalation
        var services = new ServiceCollection();
        var mediator = SetupSupportMediator();
        services.AddSingleton(mediator);
        services.AddFlowDsl();
        services.AddFlow<SupportTicketState, SupportTicketFlow>();

        var provider = services.BuildServiceProvider();
        var executor = provider.GetService<DslFlowExecutor<SupportTicketState, SupportTicketFlow>>();

        var ticket = new SupportTicketState
        {
            FlowId = "support-001",
            TicketId = "TKT-001",
            CustomerId = "CUST-001",
            Issue = "Product defective",
            Priority = "High",
            Category = "ProductQuality",
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await executor!.RunAsync(ticket);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.State.TicketAssigned.Should().BeTrue();
        result.State.AssignedAgent.Should().NotBeNullOrEmpty();
        result.State.ResolutionAttempted.Should().BeTrue();
        result.State.CustomerSatisfied.Should().BeTrue();
        result.State.TicketClosed.Should().BeTrue();

        _output.WriteLine($"✓ Support ticket resolved in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"  Ticket: {result.State.TicketId}");
        _output.WriteLine($"  Priority: {result.State.Priority}");
        _output.WriteLine($"  Assigned To: {result.State.AssignedAgent}");
        _output.WriteLine($"  Resolution Time: {(DateTime.UtcNow - result.State.CreatedAt).TotalMinutes:F1} minutes");
    }

    #endregion

    #region Helper Methods

    private ICatgaMediator SetupHighValueOrderMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<FraudDetectionCommand, bool>(Arg.Any<FraudDetectionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<AuthorizePaymentCommand, string>(Arg.Any<AuthorizePaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("AUTH-12345")));

        mediator.SendAsync<ApplyInsuranceCommand, decimal>(Arg.Any<ApplyInsuranceCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(500.00m)));

        return mediator;
    }

    private ICatgaMediator SetupBulkOrderMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<AllocateInventoryCommand, bool>(Arg.Any<AllocateInventoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<AssignWarehouseCommand, string>(Arg.Any<AssignWarehouseCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("WH-001")));

        return mediator;
    }

    private ICatgaMediator SetupInternationalOrderMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<CalculateTaxCommand, decimal>(Arg.Any<CalculateTaxCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(500.00m)));

        mediator.SendAsync<CalculateDutiesCommand, decimal>(Arg.Any<CalculateDutiesCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<decimal>>(CatgaResult<decimal>.Success(300.00m)));

        mediator.SendAsync<ObtainExportLicenseCommand, bool>(Arg.Any<ObtainExportLicenseCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupPaymentMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ProcessPaymentMethodCommand, bool>(Arg.Any<ProcessPaymentMethodCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<GenerateReceiptCommand, string>(Arg.Any<GenerateReceiptCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("RCP-12345")));

        return mediator;
    }

    private ICatgaMediator SetupRefundMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<ApproveRefundCommand, bool>(Arg.Any<ApproveRefundCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<InitiateRefundCommand, string>(Arg.Any<InitiateRefundCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("REF-12345")));

        mediator.SendAsync<RestockInventoryCommand, bool>(Arg.Any<RestockInventoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupShippingMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<GetShippingQuotesCommand, List<ShippingQuote>>(Arg.Any<GetShippingQuotesCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var quotes = new List<ShippingQuote>
                {
                    new() { Carrier = "FedEx", Cost = 25.00m, DeliveryDays = 2 },
                    new() { Carrier = "UPS", Cost = 22.00m, DeliveryDays = 3 },
                    new() { Carrier = "USPS", Cost = 15.00m, DeliveryDays = 5 }
                };
                return new ValueTask<CatgaResult<List<ShippingQuote>>>(CatgaResult<List<ShippingQuote>>.Success(quotes));
            });

        mediator.SendAsync<GenerateShippingLabelCommand, string>(Arg.Any<GenerateShippingLabelCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("TRACK-123456789")));

        return mediator;
    }

    private ICatgaMediator SetupWarehouseMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<GeneratePickListCommand, bool>(Arg.Any<GeneratePickListCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        mediator.SendAsync<SyncInventoryCommand, bool>(Arg.Any<SyncInventoryCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    private ICatgaMediator SetupSupportMediator()
    {
        var mediator = Substitute.For<ICatgaMediator>();

        mediator.SendAsync<AssignTicketCommand, string>(Arg.Any<AssignTicketCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("AGENT-001")));

        mediator.SendAsync<AttemptResolutionCommand, bool>(Arg.Any<AttemptResolutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<bool>>(CatgaResult<bool>.Success(true)));

        return mediator;
    }

    #endregion
}

// ========== Flow Configurations ==========

public class HighValueOrderFlow : FlowConfig<HighValueOrderState>
{
    protected override void Configure(IFlowBuilder<HighValueOrderState> flow)
    {
        flow.Name("high-value-order");

        flow.Send(s => new FraudDetectionCommand { Amount = s.Amount, RiskScore = s.CustomerRiskScore })
            .Into((s, r) => s.FraudCheckPassed = r.Value);

        flow.If(s => s.Amount > 10000)
            .Then(f => f.Send(s => new AuthorizePaymentCommand { Amount = s.Amount })
                .Into((s, r) => s.PaymentAuthorized = true))
            .EndIf();

        flow.Send(s => new ApplyInsuranceCommand { Amount = s.Amount })
            .Into((s, r) => s.InsurancePremium = r.Value);

        flow.Step("confirm-order", s => s.OrderConfirmed = true);
        flow.Step("apply-premium-shipping", s => s.PremiumShippingApplied = true);
    }
}

public class BulkOrderFlow : FlowConfig<BulkOrderState>
{
    protected override void Configure(IFlowBuilder<BulkOrderState> flow)
    {
        flow.Name("bulk-order");

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new AllocateInventoryCommand { ProductId = item.ProductId, Quantity = item.Quantity })
                    .Into((s, result) =>
                    {
                        if (result.Value)
                            s.AllocatedItems.Add(item.ProductId);
                    });
            })
            .OnComplete(s => s.AllItemsAllocated = true)
            .EndForEach();

        flow.Step("calculate-total", s =>
        {
            s.TotalQuantity = s.Items.Sum(i => i.Quantity);
            s.TotalAmount = s.Items.Sum(i => i.Quantity * i.UnitPrice);
            s.DiscountApplied = s.TotalAmount * s.DiscountPercentage;
        });

        flow.Send(s => new AssignWarehouseCommand { OrderId = s.OrderId })
            .Into((s, r) => s.WarehouseAssigned = r.Value);

        flow.Step("schedule-shipping", s => s.ShippingScheduled = true);
    }
}

public class InternationalOrderFlow : FlowConfig<InternationalOrderState>
{
    protected override void Configure(IFlowBuilder<InternationalOrderState> flow)
    {
        flow.Name("international-order");

        flow.Send(s => new CalculateTaxCommand { Amount = s.Amount, Country = s.DestinationCountry })
            .Into((s, r) => s.TaxAmount = r.Value);

        flow.Send(s => new CalculateDutiesCommand { Amount = s.Amount, Country = s.DestinationCountry })
            .Into((s, r) => s.DutiesAmount = r.Value);

        flow.Step("create-customs-declaration", s => s.CustomsDeclarationCreated = true);

        flow.Send(s => new ObtainExportLicenseCommand { Items = s.Items })
            .Into((s, r) => s.ExportLicenseObtained = r.Value);

        flow.Step("select-shipping-method", s => s.ShippingMethodSelected = "International Express");
    }
}

public class PaymentProcessingFlow : FlowConfig<PaymentProcessingState>
{
    protected override void Configure(IFlowBuilder<PaymentProcessingState> flow)
    {
        flow.Name("payment-processing");

        flow.ForEach(s => s.PaymentMethods)
            .Configure((method, f) =>
            {
                f.Send(s => new ProcessPaymentMethodCommand { Method = method, Amount = method.Amount })
                    .Into((s, result) =>
                    {
                        method.Processed = result.Value;
                        if (result.Value)
                            s.TotalProcessed += method.Amount;
                    });
            })
            .OnComplete(s => s.AllPaymentsSuccessful = s.PaymentMethods.All(m => m.Processed))
            .EndForEach();

        flow.Send(s => new GenerateReceiptCommand { TransactionId = s.TransactionId, Amount = s.Amount })
            .Into((s, r) => s.ReceiptGenerated = true);

        flow.Step("send-confirmation-email", s => s.ConfirmationEmailSent = true);
    }
}

public class RefundProcessingFlow : FlowConfig<RefundProcessingState>
{
    protected override void Configure(IFlowBuilder<RefundProcessingState> flow)
    {
        flow.Name("refund-processing");

        flow.Send(s => new ApproveRefundCommand { OrderId = s.OriginalOrderId, Amount = s.RefundAmount })
            .Into((s, r) => s.RefundApproved = r.Value);

        flow.ForEach(s => s.Items)
            .Configure((item, f) =>
            {
                f.Send(s => new RestockInventoryCommand { ProductId = item.ProductId, Quantity = item.Quantity })
                    .Into((s, result) => s.ItemsProcessed++);
            })
            .OnComplete(s => s.InventoryRestocked = true)
            .EndForEach();

        flow.Send(s => new InitiateRefundCommand { Amount = s.RefundAmount })
            .Into((s, r) => s.RefundInitiated = true);

        flow.Step("notify-customer", s => s.CustomerNotified = true);
    }
}

public class ShippingOptimizationFlow : FlowConfig<ShippingOptimizationState>
{
    protected override void Configure(IFlowBuilder<ShippingOptimizationState> flow)
    {
        flow.Name("shipping-optimization");

        flow.Send(s => new GetShippingQuotesCommand { Weight = s.Weight, Origin = s.OriginZipCode, Destination = s.DestinationZipCode })
            .Into((s, r) =>
            {
                s.CarrierQuotesReceived = r.Value?.Count ?? 0;
                if (r.Value?.Any() == true)
                {
                    var optimal = r.Value.OrderBy(q => q.Cost).First();
                    s.OptimalCarrierSelected = optimal.Carrier;
                    s.ShippingCost = optimal.Cost;
                    s.EstimatedDelivery = DateTime.UtcNow.AddDays(optimal.DeliveryDays);
                }
            });

        flow.Send(s => new GenerateShippingLabelCommand { OrderId = s.OrderId, Carrier = s.OptimalCarrierSelected })
            .Into((s, r) =>
            {
                s.LabelGenerated = true;
                s.TrackingNumber = r.Value;
            });
    }
}

public class WarehouseManagementFlow : FlowConfig<WarehouseManagementState>
{
    protected override void Configure(IFlowBuilder<WarehouseManagementState> flow)
    {
        flow.Name("warehouse-management");

        flow.ForEach(s => s.Orders)
            .Configure((order, f) =>
            {
                f.Send(s => new GeneratePickListCommand { OrderId = order.OrderId })
                    .Into((s, result) =>
                    {
                        if (result.Value)
                        {
                            s.OrdersProcessed++;
                            s.PickListsGenerated++;
                        }
                    });
            })
            .OnComplete(s => s.PackingStarted = true)
            .EndForEach();

        flow.Send(s => new SyncInventoryCommand { WarehouseId = s.WarehouseId })
            .Into((s, r) => s.InventorySynced = r.Value);

        flow.Step("schedule-shipments", s => s.ShipmentsScheduled = s.OrdersProcessed);
    }
}

public class SupportTicketFlow : FlowConfig<SupportTicketState>
{
    protected override void Configure(IFlowBuilder<SupportTicketState> flow)
    {
        flow.Name("support-ticket");

        flow.Send(s => new AssignTicketCommand { TicketId = s.TicketId, Priority = s.Priority })
            .Into((s, r) =>
            {
                s.TicketAssigned = true;
                s.AssignedAgent = r.Value;
            });

        flow.Send(s => new AttemptResolutionCommand { TicketId = s.TicketId, Issue = s.Issue })
            .Into((s, r) => s.ResolutionAttempted = r.Value);

        flow.Step("check-satisfaction", s => s.CustomerSatisfied = true);
        flow.Step("close-ticket", s => s.TicketClosed = true);
    }
}

// ========== States ==========

public class HighValueOrderState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public List<OrderItemDetail> Items { get; set; } = new();
    public decimal CustomerRiskScore { get; set; }
    public AddressDetail ShippingAddress { get; set; } = new();
    public bool FraudCheckPassed { get; set; }
    public bool ManualReviewRequired { get; set; }
    public bool PaymentAuthorized { get; set; }
    public bool OrderConfirmed { get; set; }
    public bool InsuranceApplied { get; set; }
    public decimal InsurancePremium { get; set; }
    public bool PremiumShippingApplied { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class BulkOrderState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public List<BulkOrderItem> Items { get; set; } = new();
    public string OrderType { get; set; } = string.Empty;
    public decimal DiscountPercentage { get; set; }
    public List<string> AllocatedItems { get; set; } = new();
    public bool AllItemsAllocated { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DiscountApplied { get; set; }
    public string WarehouseAssigned { get; set; } = string.Empty;
    public bool ShippingScheduled { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class InternationalOrderState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string DestinationCountry { get; set; } = string.Empty;
    public string OriginCountry { get; set; } = string.Empty;
    public List<OrderItemDetail> Items { get; set; } = new();
    public bool CustomsDeclarationCreated { get; set; }
    public bool TaxCalculated { get; set; }
    public decimal TaxAmount { get; set; }
    public bool DutiesCalculated { get; set; }
    public decimal DutiesAmount { get; set; }
    public bool ExportLicenseObtained { get; set; }
    public string ShippingMethodSelected { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class PaymentProcessingState : IFlowState
{
    public string? FlowId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public List<PaymentMethod> PaymentMethods { get; set; } = new();
    public string Currency { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public decimal TotalProcessed { get; set; }
    public bool AllPaymentsSuccessful { get; set; }
    public bool ReceiptGenerated { get; set; }
    public bool ConfirmationEmailSent { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class RefundProcessingState : IFlowState
{
    public string? FlowId { get; set; }
    public string OriginalOrderId { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string RefundReason { get; set; } = string.Empty;
    public List<RefundItem> Items { get; set; } = new();
    public bool RefundApproved { get; set; }
    public int ItemsProcessed { get; set; }
    public bool InventoryRestocked { get; set; }
    public bool RefundInitiated { get; set; }
    public bool CustomerNotified { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class ShippingOptimizationState : IFlowState
{
    public string? FlowId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public Dimensions Dimensions { get; set; } = new();
    public string OriginZipCode { get; set; } = string.Empty;
    public string DestinationZipCode { get; set; } = string.Empty;
    public DateTime DeliveryDeadline { get; set; }
    public string ShippingPreference { get; set; } = string.Empty;
    public int CarrierQuotesReceived { get; set; }
    public string OptimalCarrierSelected { get; set; } = string.Empty;
    public decimal ShippingCost { get; set; }
    public DateTime EstimatedDelivery { get; set; }
    public bool LabelGenerated { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class WarehouseManagementState : IFlowState
{
    public string? FlowId { get; set; }
    public string WarehouseId { get; set; } = string.Empty;
    public List<WarehouseOrder> Orders { get; set; } = new();
    public int OrdersProcessed { get; set; }
    public bool InventorySynced { get; set; }
    public int PickListsGenerated { get; set; }
    public bool PackingStarted { get; set; }
    public int ShipmentsScheduled { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

public class SupportTicketState : IFlowState
{
    public string? FlowId { get; set; }
    public string TicketId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool TicketAssigned { get; set; }
    public string AssignedAgent { get; set; } = string.Empty;
    public bool ResolutionAttempted { get; set; }
    public bool CustomerSatisfied { get; set; }
    public bool TicketClosed { get; set; }

    public bool HasChanges => true;
    public int GetChangedMask() => 0;
    public bool IsFieldChanged(int fieldIndex) => false;
    public void ClearChanges() { }
    public void MarkChanged(int fieldIndex) { }
    public IEnumerable<string> GetChangedFieldNames() { yield break; }
}

// ========== Supporting Models ==========

public class OrderItemDetail
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class AddressDetail
{
    public string Country { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class BulkOrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

public class PaymentMethod
{
    public string Type { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CardLast4 { get; set; } = string.Empty;
    public string AccountEmail { get; set; } = string.Empty;
    public bool Processed { get; set; }
}

public class RefundItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal RefundAmount { get; set; }
}

public class Dimensions
{
    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Height { get; set; }
}

public class ShippingQuote
{
    public string Carrier { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public int DeliveryDays { get; set; }
}

public class WarehouseOrder
{
    public string OrderId { get; set; } = string.Empty;
    public List<WarehouseOrderItem> Items { get; set; } = new();
}

public class WarehouseOrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

// ========== Commands ==========

public class FraudDetectionCommand : IRequest<bool> { public decimal Amount { get; set; } public decimal RiskScore { get; set; } }
public class AuthorizePaymentCommand : IRequest<string> { public decimal Amount { get; set; } }
public class ApplyInsuranceCommand : IRequest<decimal> { public decimal Amount { get; set; } }
public class AllocateInventoryCommand : IRequest<bool> { public string ProductId { get; set; } = string.Empty; public int Quantity { get; set; } }
public class AssignWarehouseCommand : IRequest<string> { public string OrderId { get; set; } = string.Empty; }
public class CalculateTaxCommand : IRequest<decimal> { public decimal Amount { get; set; } public string Country { get; set; } = string.Empty; }
public class CalculateDutiesCommand : IRequest<decimal> { public decimal Amount { get; set; } public string Country { get; set; } = string.Empty; }
public class ObtainExportLicenseCommand : IRequest<bool> { public List<OrderItemDetail> Items { get; set; } = new(); }
public class ProcessPaymentMethodCommand : IRequest<bool> { public PaymentMethod Method { get; set; } = new(); public decimal Amount { get; set; } }
public class GenerateReceiptCommand : IRequest<string> { public string TransactionId { get; set; } = string.Empty; public decimal Amount { get; set; } }
public class ApproveRefundCommand : IRequest<bool> { public string OrderId { get; set; } = string.Empty; public decimal Amount { get; set; } }
public class InitiateRefundCommand : IRequest<string> { public decimal Amount { get; set; } }
public class RestockInventoryCommand : IRequest<bool> { public string ProductId { get; set; } = string.Empty; public int Quantity { get; set; } }
public class GetShippingQuotesCommand : IRequest<List<ShippingQuote>> { public decimal Weight { get; set; } public string Origin { get; set; } = string.Empty; public string Destination { get; set; } = string.Empty; }
public class GenerateShippingLabelCommand : IRequest<string> { public string OrderId { get; set; } = string.Empty; public string Carrier { get; set; } = string.Empty; }
public class GeneratePickListCommand : IRequest<bool> { public string OrderId { get; set; } = string.Empty; }
public class SyncInventoryCommand : IRequest<bool> { public string WarehouseId { get; set; } = string.Empty; }
public class AssignTicketCommand : IRequest<string> { public string TicketId { get; set; } = string.Empty; public string Priority { get; set; } = string.Empty; }
public class AttemptResolutionCommand : IRequest<bool> { public string TicketId { get; set; } = string.Empty; public string Issue { get; set; } = string.Empty; }
