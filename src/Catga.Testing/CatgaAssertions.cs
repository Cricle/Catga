using Catga.Core;
using Catga.Flow.Dsl;

namespace Catga.Testing;

/// <summary>
/// Fluent assertion helpers for Catga results and flow outcomes.
/// Framework-agnostic (works with xUnit, NUnit, MSTest).
/// </summary>
public static class CatgaAssertions
{
    public static CatgaResult<T> ShouldSucceed<T>(this CatgaResult<T> result)
    {
        if (!result.IsSuccess)
            throw new CatgaAssertionException($"Expected success but got failure: {result.Error} [{result.ErrorCode}]");
        return result;
    }

    public static CatgaResult<T> ShouldFail<T>(this CatgaResult<T> result)
    {
        if (result.IsSuccess)
            throw new CatgaAssertionException("Expected failure but got success");
        return result;
    }

    public static CatgaResult<T> ShouldFailWith<T>(this CatgaResult<T> result, string errorCode)
    {
        result.ShouldFail();
        if (result.ErrorCode != errorCode)
            throw new CatgaAssertionException($"Expected error code '{errorCode}' but got '{result.ErrorCode}'");
        return result;
    }

    public static CatgaResult<T> ShouldHaveValue<T>(this CatgaResult<T> result, T expected)
    {
        result.ShouldSucceed();
        if (!EqualityComparer<T>.Default.Equals(result.Value, expected))
            throw new CatgaAssertionException($"Expected value '{expected}' but got '{result.Value}'");
        return result;
    }

    public static DslFlowResult<TState> ShouldComplete<TState>(this DslFlowResult<TState> result)
        where TState : class
    {
        if (!result.IsSuccess)
            throw new CatgaAssertionException($"Expected flow to complete but got: {result.Error} (status: {result.Status})");
        return result;
    }

    public static DslFlowResult<TState> ShouldFail<TState>(this DslFlowResult<TState> result)
        where TState : class
    {
        if (result.IsSuccess)
            throw new CatgaAssertionException("Expected flow to fail but it succeeded");
        return result;
    }

    public static DslFlowResult<TState> ShouldBeSuspended<TState>(this DslFlowResult<TState> result)
        where TState : class
    {
        if (result.Status != DslFlowStatus.Suspended)
            throw new CatgaAssertionException($"Expected flow to be suspended but status is: {result.Status}");
        return result;
    }

    public static IEnumerable<T> ShouldContain<T>(this IEnumerable<T> messages, Func<T, bool>? predicate = null)
    {
        var list = messages.ToList();
        var matches = predicate != null ? list.Where(predicate).ToList() : list;
        if (!matches.Any())
            throw new CatgaAssertionException($"Expected at least one {typeof(T).Name} but found none");
        return matches;
    }
}

public sealed class CatgaAssertionException : Exception
{
    public CatgaAssertionException(string message) : base(message) { }
}
