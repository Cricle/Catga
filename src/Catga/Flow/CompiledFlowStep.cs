using System.Linq.Expressions;
using Catga.Abstractions;

namespace Catga.Flow.Dsl;

/// <summary>
/// Compiled flow step with serializable execution logic.
/// Replaces dynamic delegates with compiled expression trees.
/// </summary>
public abstract class CompiledFlowStep
{
    /// <summary>Execute the request factory with compiled code instead of DynamicInvoke.</summary>
    public abstract object? ExecuteRequestFactory(IFlowState state);

    /// <summary>Execute the condition factory with compiled code instead of DynamicInvoke.</summary>
    public abstract bool ExecuteCondition(IFlowState state);

    /// <summary>Execute the fail condition factory with compiled code instead of DynamicInvoke.</summary>
    public abstract bool ExecuteFailCondition(object? result);

    /// <summary>Execute the result setter with compiled code instead of DynamicInvoke.</summary>
    public abstract void ExecuteResultSetter(IFlowState state, object? result);

    /// <summary>Execute the compensation factory with compiled code instead of DynamicInvoke.</summary>
    public abstract object? ExecuteCompensation(IFlowState state);

    /// <summary>Execute the collection selector with compiled code instead of DynamicInvoke.</summary>
    public abstract System.Collections.IEnumerable? ExecuteCollectionSelector(IFlowState state);

    /// <summary>Execute the loop condition with compiled code instead of DynamicInvoke.</summary>
    public abstract bool ExecuteLoopCondition(IFlowState state);

    /// <summary>Execute the repeat count selector with compiled code instead of DynamicInvoke.</summary>
    public abstract int ExecuteRepeatCountSelector(IFlowState state);

    /// <summary>Execute the branch condition with compiled code instead of DynamicInvoke.</summary>
    public abstract bool ExecuteBranchCondition(IFlowState state);

    /// <summary>Execute the switch selector with compiled code instead of DynamicInvoke.</summary>
    public abstract object? ExecuteSwitchSelector(IFlowState state);

    /// <summary>Execute the on item success callback with compiled code instead of DynamicInvoke.</summary>
    public abstract void ExecuteOnItemSuccess(IFlowState state, object? item, object? result);

    /// <summary>Execute the on item fail callback with compiled code instead of DynamicInvoke.</summary>
    public abstract void ExecuteOnItemFail(IFlowState state, object? item, string? error);

    /// <summary>Execute the on complete callback with compiled code instead of DynamicInvoke.</summary>
    public abstract void ExecuteOnComplete(IFlowState state);
}

/// <summary>
/// Default implementation for flow steps without compiled code.
/// Used when source generator hasn't generated specific implementations.
/// </summary>
public class DefaultCompiledFlowStep : CompiledFlowStep
{
    private readonly FlowStep _step;

    public DefaultCompiledFlowStep(FlowStep step)
    {
        _step = step;
    }

    public override object? ExecuteRequestFactory(IFlowState state)
    {
        if (_step.RequestFactory == null)
            return null;

        // Fallback to DynamicInvoke only when no compiled code is available
        // This should be replaced by source-generated code
        return _step.RequestFactory.DynamicInvoke(state);
    }

    public override bool ExecuteCondition(IFlowState state)
    {
        if (_step.ConditionFactory == null)
            return true;

        return (bool)(_step.ConditionFactory.DynamicInvoke(state) ?? true);
    }

    public override bool ExecuteFailCondition(object? result)
    {
        if (_step.FailConditionFactory == null || result == null)
            return false;

        return (bool)(_step.FailConditionFactory.DynamicInvoke(result) ?? false);
    }

    public override void ExecuteResultSetter(IFlowState state, object? result)
    {
        if (_step.ResultSetter != null && result != null)
        {
            _step.ResultSetter.DynamicInvoke(state, result);
        }
    }

    public override object? ExecuteCompensation(IFlowState state)
    {
        if (_step.CompensationFactory == null)
            return null;

        return _step.CompensationFactory.DynamicInvoke(state);
    }

    public override System.Collections.IEnumerable? ExecuteCollectionSelector(IFlowState state)
    {
        if (_step.CollectionSelector == null)
            return null;

        return _step.CollectionSelector.DynamicInvoke(state) as System.Collections.IEnumerable;
    }

    public override bool ExecuteLoopCondition(IFlowState state)
    {
        if (_step.LoopCondition == null)
            return true;

        return (bool)(_step.LoopCondition.DynamicInvoke(state) ?? true);
    }

    public override int ExecuteRepeatCountSelector(IFlowState state)
    {
        if (_step.RepeatCountSelector == null)
            return _step.RepeatCount ?? 1;

        return (int)(_step.RepeatCountSelector.DynamicInvoke(state) ?? 1);
    }

    public override bool ExecuteBranchCondition(IFlowState state)
    {
        if (_step.BranchCondition == null)
            return true;

        return (bool)(_step.BranchCondition.DynamicInvoke(state) ?? true);
    }

    public override object? ExecuteSwitchSelector(IFlowState state)
    {
        if (_step.SwitchSelector == null)
            return null;

        return _step.SwitchSelector.DynamicInvoke(state);
    }

    public override void ExecuteOnItemSuccess(IFlowState state, object? item, object? result)
    {
        if (_step.OnItemSuccess != null)
        {
            try
            {
                _step.OnItemSuccess.DynamicInvoke(state, item, result);
            }
            catch
            {
                // Ignore callback errors
            }
        }
    }

    public override void ExecuteOnItemFail(IFlowState state, object? item, string? error)
    {
        if (_step.OnItemFail != null)
        {
            try
            {
                _step.OnItemFail.DynamicInvoke(state, item, error);
            }
            catch
            {
                // Ignore callback errors
            }
        }
    }

    public override void ExecuteOnComplete(IFlowState state)
    {
        if (_step.OnComplete != null)
        {
            try
            {
                _step.OnComplete.DynamicInvoke(state);
            }
            catch
            {
                // Ignore callback errors
            }
        }
    }
}
