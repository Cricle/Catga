using Catga.Flow.Dsl;
using Xunit;
using Xunit.Abstractions;

namespace Catga.Tests.Flow;

public class DebugFlowStepProperties
{
    private readonly ITestOutputHelper _output;

    public DebugFlowStepProperties(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ListAllFlowStepProperties()
    {
        var stepType = typeof(FlowStep);
        var allProperties = stepType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        _output.WriteLine("All FlowStep properties:");
        foreach (var prop in allProperties.OrderBy(p => p.Name))
        {
            _output.WriteLine($"- {prop.Name} ({prop.PropertyType.Name}) - {(prop.GetMethod?.IsPublic == true ? "Public" : "Internal")}");
        }
    }
}
