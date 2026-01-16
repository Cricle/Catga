using Catga.Persistence.InMemory.Flow;

namespace Catga.Tests.Helpers;

/// <summary>
/// Extension methods for creating test stores.
/// </summary>
public static class TestStoreExtensions
{
    /// <summary>
    /// Creates a new InMemoryDslFlowStore.
    /// </summary>
    public static InMemoryDslFlowStore CreateTestFlowStore()
    {
        return new InMemoryDslFlowStore();
    }
}
