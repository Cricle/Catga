using Catga.Core;
using FluentAssertions;
using FluentAssertions.Execution;
using FluentAssertions.Primitives;

namespace Catga.Testing.Assertions;

/// <summary>
/// CatgaResult FluentAssertions 扩展
/// </summary>
public static class CatgaAssertionExtensions
{
    public static CatgaResultAssertions<T> Should<T>(this CatgaResult<T> result)
    {
        return new CatgaResultAssertions<T>(result);
    }
}

/// <summary>
/// CatgaResult 断言类
/// </summary>
public class CatgaResultAssertions<T> : ReferenceTypeAssertions<CatgaResult<T>, CatgaResultAssertions<T>>
{
    public CatgaResultAssertions(CatgaResult<T> subject) : base(subject)
    {
    }

    protected override string Identifier => "CatgaResult";

    /// <summary>
    /// 断言结果成功
    /// </summary>
    public AndConstraint<CatgaResultAssertions<T>> BeSuccessful(string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .ForCondition(Subject.IsSuccess)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to be successful{reason}, but it was a failure with error: {0}.",
                Subject.Error);

        return new AndConstraint<CatgaResultAssertions<T>>(this);
    }

    /// <summary>
    /// 断言结果失败
    /// </summary>
    public AndConstraint<CatgaResultAssertions<T>> BeFailure(string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .ForCondition(!Subject.IsSuccess)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to be a failure{reason}, but it was successful.");

        return new AndConstraint<CatgaResultAssertions<T>>(this);
    }

    /// <summary>
    /// 断言结果失败且包含特定错误消息
    /// </summary>
    public AndConstraint<CatgaResultAssertions<T>> BeFailureWithError(string expectedError, string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .ForCondition(!Subject.IsSuccess)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to be a failure{reason}, but it was successful.")
            .Then
            .ForCondition(Subject.Error?.Contains(expectedError) == true)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to have error containing {0}{reason}, but found {1}.",
                expectedError, Subject.Error);

        return new AndConstraint<CatgaResultAssertions<T>>(this);
    }

    /// <summary>
    /// 断言结果值
    /// </summary>
    public AndConstraint<CatgaResultAssertions<T>> HaveValue(T expectedValue, string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .ForCondition(Subject.IsSuccess)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to be successful{reason}, but it was a failure.")
            .Then
            .ForCondition(EqualityComparer<T>.Default.Equals(Subject.Value, expectedValue))
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to have value {0}{reason}, but found {1}.",
                expectedValue, Subject.Value);

        return new AndConstraint<CatgaResultAssertions<T>>(this);
    }

    /// <summary>
    /// 断言结果值满足条件
    /// </summary>
    public AndConstraint<CatgaResultAssertions<T>> HaveValueSatisfying(Action<T> assertion, string because = "", params object[] becauseArgs)
    {
        Execute.Assertion
            .ForCondition(Subject.IsSuccess)
            .BecauseOf(because, becauseArgs)
            .FailWith("Expected {context:result} to be successful{reason}, but it was a failure.");

        if (Subject.Value != null)
        {
            assertion(Subject.Value);
        }

        return new AndConstraint<CatgaResultAssertions<T>>(this);
    }
}

