using Xunit;
using Xunit.Sdk;

namespace Catga.Tests;

/// <summary>
/// Fact attribute with default timeout of 30 seconds.
/// </summary>
public class TimeoutFactAttribute : FactAttribute
{
    public TimeoutFactAttribute(int timeoutMs = 30000)
    {
        Timeout = timeoutMs;
    }
}

/// <summary>
/// Theory attribute with default timeout of 30 seconds.
/// </summary>
public class TimeoutTheoryAttribute : TheoryAttribute
{
    public TimeoutTheoryAttribute(int timeoutMs = 30000)
    {
        Timeout = timeoutMs;
    }
}






