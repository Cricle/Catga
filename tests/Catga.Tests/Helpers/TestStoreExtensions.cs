using Catga.Persistence.InMemory.Flow;

namespace Catga.Tests.Helpers;

/// <summary>
/// Extension methods for creating test stores with default serializers.
/// </summary>
public static class TestStoreExtensions
{
    private static readonly TestMessageSerializer DefaultSerializer = new();
    
    /// <summary>
    /// Creates a new InMemoryDslFlowStore with a default test serializer.
    /// </summary>
    public static InMemoryDslFlowStore CreateTestFlowStore()
    {
        return new InMemoryDslFlowStore(DefaultSerializer);
    }
}
