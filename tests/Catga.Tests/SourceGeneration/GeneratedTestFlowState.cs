using Catga.Flow;

namespace Catga.Tests.SourceGeneration;

[FlowState]
public partial class GeneratedTestFlowState : IFlowState
{
    public string? FlowId { get; set; }

    [FlowStateField]
    private string _name = string.Empty;

    [FlowStateField]
    private int _value;

    public string Name
    {
        get => _name;
        set => _name = value;
    }

    public int Value
    {
        get => _value;
        set => _value = value;
    }
}
