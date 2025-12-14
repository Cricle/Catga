using Catga.Observability;
using FluentAssertions;

namespace Catga.Tests.Observability;

public class CatgaDiagnosticsTests
{
    [Fact]
    public void ActivitySource_IsNotNull()
    {
        CatgaDiagnostics.ActivitySource.Should().NotBeNull();
    }

    [Fact]
    public void ActivitySource_HasName()
    {
        CatgaDiagnostics.ActivitySource.Name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ActivitySource_IsSameInstance()
    {
        var source1 = CatgaDiagnostics.ActivitySource;
        var source2 = CatgaDiagnostics.ActivitySource;

        source1.Should().BeSameAs(source2);
    }
}
