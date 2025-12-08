using Catga.Flow.Dsl;
using FluentAssertions;

namespace Catga.Tests.Flow;

/// <summary>
/// TDD tests for IFlowState change tracking.
/// Source generator will generate implementation for [FlowState] attributed classes.
/// </summary>
public class FlowStateChangeTrackingTests
{
    #region Basic Change Tracking

    [Fact]
    public void NewState_HasNoChanges()
    {
        var state = new TestFlowState();

        state.HasChanges.Should().BeFalse();
        state.GetChangedMask().Should().Be(0);
    }

    [Fact]
    public void SetProperty_MarksAsChanged()
    {
        var state = new TestFlowState();

        state.OrderId = "order-123";

        state.HasChanges.Should().BeTrue();
        state.IsFieldChanged(TestFlowState.Field_OrderId).Should().BeTrue();
    }

    [Fact]
    public void SetMultipleProperties_TracksAllChanges()
    {
        var state = new TestFlowState();

        state.OrderId = "order-123";
        state.Amount = 100.50m;
        state.Status = "Pending";

        state.HasChanges.Should().BeTrue();
        state.IsFieldChanged(TestFlowState.Field_OrderId).Should().BeTrue();
        state.IsFieldChanged(TestFlowState.Field_Amount).Should().BeTrue();
        state.IsFieldChanged(TestFlowState.Field_Status).Should().BeTrue();
    }

    [Fact]
    public void ClearChanges_ResetsAllFlags()
    {
        var state = new TestFlowState
        {
            OrderId = "order-123",
            Amount = 100.50m
        };

        state.ClearChanges();

        state.HasChanges.Should().BeFalse();
        state.GetChangedMask().Should().Be(0);
        state.IsFieldChanged(TestFlowState.Field_OrderId).Should().BeFalse();
        state.IsFieldChanged(TestFlowState.Field_Amount).Should().BeFalse();
    }

    [Fact]
    public void GetChangedFieldNames_ReturnsOnlyChangedFields()
    {
        var state = new TestFlowState
        {
            OrderId = "order-123",
            Status = "Pending"
        };

        var changedFields = state.GetChangedFieldNames().ToList();

        changedFields.Should().Contain(nameof(TestFlowState.OrderId));
        changedFields.Should().Contain(nameof(TestFlowState.Status));
        changedFields.Should().NotContain(nameof(TestFlowState.Amount));
        changedFields.Should().NotContain(nameof(TestFlowState.PaymentId));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SetSameValue_StillMarksAsChanged()
    {
        var state = new TestFlowState { OrderId = "order-123" };
        state.ClearChanges();

        state.OrderId = "order-123"; // Same value

        // Still marks as changed (no value comparison)
        state.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void SetNull_MarksAsChanged()
    {
        var state = new TestFlowState { OrderId = "order-123" };
        state.ClearChanges();

        state.OrderId = null;

        state.HasChanges.Should().BeTrue();
        state.IsFieldChanged(TestFlowState.Field_OrderId).Should().BeTrue();
    }

    [Fact]
    public void MultipleSetsSameField_OnlyOneChange()
    {
        var state = new TestFlowState();

        state.OrderId = "order-1";
        state.OrderId = "order-2";
        state.OrderId = "order-3";

        state.HasChanges.Should().BeTrue();
        state.GetChangedFieldNames().Count().Should().Be(1);
    }

    [Fact]
    public void FieldIndex_IsUnique()
    {
        TestFlowState.Field_OrderId.Should().NotBe(TestFlowState.Field_Amount);
        TestFlowState.Field_Amount.Should().NotBe(TestFlowState.Field_Status);
        TestFlowState.Field_Status.Should().NotBe(TestFlowState.Field_PaymentId);
    }

    [Fact]
    public void FieldCount_MatchesPropertyCount()
    {
        TestFlowState.FieldCount.Should().Be(4);
    }

    #endregion

    #region FlowId Property

    [Fact]
    public void FlowId_CanBeSetAndRetrieved()
    {
        var state = new TestFlowState();

        state.FlowId = "flow-123";

        state.FlowId.Should().Be("flow-123");
    }

    [Fact]
    public void FlowId_IsNotTrackedAsChange()
    {
        var state = new TestFlowState();

        state.FlowId = "flow-123";

        // FlowId is infrastructure, not business state
        state.GetChangedFieldNames().Should().NotContain(nameof(state.FlowId));
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void MaxFields_32FieldsSupported()
    {
        // Using int mask, max 32 fields
        var state = new LargeFlowState();

        state.Field01 = "value";
        state.Field32 = "value";

        state.IsFieldChanged(0).Should().BeTrue();
        state.IsFieldChanged(31).Should().BeTrue();
    }

    [Fact]
    public void GetChangedMask_ReturnsBitmask()
    {
        var state = new TestFlowState();

        state.OrderId = "order-123"; // Field 0
        state.Status = "Pending";    // Field 2

        var mask = state.GetChangedMask();

        // Bit 0 and Bit 2 should be set
        (mask & (1 << TestFlowState.Field_OrderId)).Should().NotBe(0);
        (mask & (1 << TestFlowState.Field_Status)).Should().NotBe(0);
        (mask & (1 << TestFlowState.Field_Amount)).Should().Be(0);
    }

    #endregion
}

#region Test Flow States

/// <summary>
/// Test flow state - will be generated by source generator.
/// For now, manually implement IFlowState for testing.
/// </summary>
public class TestFlowState : IFlowState
{
    // Field indices (generated)
    public const int Field_OrderId = 0;
    public const int Field_Amount = 1;
    public const int Field_Status = 2;
    public const int Field_PaymentId = 3;
    public const int FieldCount = 4;

    private int _changedMask;

    // Infrastructure
    public string? FlowId { get; set; }

    // Business state
    private string? _orderId;
    public string? OrderId
    {
        get => _orderId;
        set { _orderId = value; MarkChanged(Field_OrderId); }
    }

    private decimal _amount;
    public decimal Amount
    {
        get => _amount;
        set { _amount = value; MarkChanged(Field_Amount); }
    }

    private string? _status;
    public string? Status
    {
        get => _status;
        set { _status = value; MarkChanged(Field_Status); }
    }

    private string? _paymentId;
    public string? PaymentId
    {
        get => _paymentId;
        set { _paymentId = value; MarkChanged(Field_PaymentId); }
    }

    // IFlowState implementation
    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);

    public IEnumerable<string> GetChangedFieldNames()
    {
        if (IsFieldChanged(Field_OrderId)) yield return nameof(OrderId);
        if (IsFieldChanged(Field_Amount)) yield return nameof(Amount);
        if (IsFieldChanged(Field_Status)) yield return nameof(Status);
        if (IsFieldChanged(Field_PaymentId)) yield return nameof(PaymentId);
    }
}

/// <summary>
/// Large flow state for boundary testing (32 fields max with int mask).
/// </summary>
public class LargeFlowState : IFlowState
{
    private int _changedMask;

    public string? FlowId { get; set; }

    private string? _field01; public string? Field01 { get => _field01; set { _field01 = value; MarkChanged(0); } }
    private string? _field02; public string? Field02 { get => _field02; set { _field02 = value; MarkChanged(1); } }
    private string? _field03; public string? Field03 { get => _field03; set { _field03 = value; MarkChanged(2); } }
    private string? _field04; public string? Field04 { get => _field04; set { _field04 = value; MarkChanged(3); } }
    private string? _field05; public string? Field05 { get => _field05; set { _field05 = value; MarkChanged(4); } }
    private string? _field06; public string? Field06 { get => _field06; set { _field06 = value; MarkChanged(5); } }
    private string? _field07; public string? Field07 { get => _field07; set { _field07 = value; MarkChanged(6); } }
    private string? _field08; public string? Field08 { get => _field08; set { _field08 = value; MarkChanged(7); } }
    private string? _field09; public string? Field09 { get => _field09; set { _field09 = value; MarkChanged(8); } }
    private string? _field10; public string? Field10 { get => _field10; set { _field10 = value; MarkChanged(9); } }
    private string? _field11; public string? Field11 { get => _field11; set { _field11 = value; MarkChanged(10); } }
    private string? _field12; public string? Field12 { get => _field12; set { _field12 = value; MarkChanged(11); } }
    private string? _field13; public string? Field13 { get => _field13; set { _field13 = value; MarkChanged(12); } }
    private string? _field14; public string? Field14 { get => _field14; set { _field14 = value; MarkChanged(13); } }
    private string? _field15; public string? Field15 { get => _field15; set { _field15 = value; MarkChanged(14); } }
    private string? _field16; public string? Field16 { get => _field16; set { _field16 = value; MarkChanged(15); } }
    private string? _field17; public string? Field17 { get => _field17; set { _field17 = value; MarkChanged(16); } }
    private string? _field18; public string? Field18 { get => _field18; set { _field18 = value; MarkChanged(17); } }
    private string? _field19; public string? Field19 { get => _field19; set { _field19 = value; MarkChanged(18); } }
    private string? _field20; public string? Field20 { get => _field20; set { _field20 = value; MarkChanged(19); } }
    private string? _field21; public string? Field21 { get => _field21; set { _field21 = value; MarkChanged(20); } }
    private string? _field22; public string? Field22 { get => _field22; set { _field22 = value; MarkChanged(21); } }
    private string? _field23; public string? Field23 { get => _field23; set { _field23 = value; MarkChanged(22); } }
    private string? _field24; public string? Field24 { get => _field24; set { _field24 = value; MarkChanged(23); } }
    private string? _field25; public string? Field25 { get => _field25; set { _field25 = value; MarkChanged(24); } }
    private string? _field26; public string? Field26 { get => _field26; set { _field26 = value; MarkChanged(25); } }
    private string? _field27; public string? Field27 { get => _field27; set { _field27 = value; MarkChanged(26); } }
    private string? _field28; public string? Field28 { get => _field28; set { _field28 = value; MarkChanged(27); } }
    private string? _field29; public string? Field29 { get => _field29; set { _field29 = value; MarkChanged(28); } }
    private string? _field30; public string? Field30 { get => _field30; set { _field30 = value; MarkChanged(29); } }
    private string? _field31; public string? Field31 { get => _field31; set { _field31 = value; MarkChanged(30); } }
    private string? _field32; public string? Field32 { get => _field32; set { _field32 = value; MarkChanged(31); } }

    public bool HasChanges => _changedMask != 0;
    public int GetChangedMask() => _changedMask;
    public bool IsFieldChanged(int fieldIndex) => (_changedMask & (1 << fieldIndex)) != 0;
    public void ClearChanges() => _changedMask = 0;
    public void MarkChanged(int fieldIndex) => _changedMask |= (1 << fieldIndex);

    public IEnumerable<string> GetChangedFieldNames()
    {
        for (int i = 0; i < 32; i++)
        {
            if (IsFieldChanged(i))
                yield return $"Field{i + 1:D2}";
        }
    }
}

#endregion






