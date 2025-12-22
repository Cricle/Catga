using Catga.EventSourcing;
using Catga.Persistence.InMemory.Stores;
using Catga.Tests.PropertyTests.Generators;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// InMemorySnapshotStore 属性测试
/// 使用 FsCheck 进行属性测试验证
/// 
/// 注意: FsCheck.Xunit 的 [Property] 特性要求测试类有无参构造函数
/// </summary>
[Trait("Category", "Property")]
[Trait("Backend", "InMemory")]
public class InMemorySnapshotStorePropertyTests
{
    /// <summary>
    /// Property 5: SnapshotStore Round-Trip Consistency
    /// 
    /// *For any* valid snapshot (aggregate state, stream ID, and version), saving then loading 
    /// SHALL return a snapshot with identical StreamId, Version, and State data.
    /// 
    /// **Validates: Requirements 2.13**
    /// 
    /// Feature: tdd-validation, Property 5: SnapshotStore Round-Trip Consistency
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property SnapshotStore_RoundTrip_PreservesAllSnapshotData()
    {
        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            SnapshotGenerators.TestAggregateStateArbitrary(),
            SnapshotGenerators.SnapshotVersionArbitrary(),
            (streamId, state, version) =>
            {
                // Arrange
                var store = new EnhancedInMemorySnapshotStore();

                // Act
                store.SaveAsync(streamId, state, version).AsTask().GetAwaiter().GetResult();
                var loaded = store.LoadAsync<TestAggregateState>(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Verify round-trip consistency
                if (loaded == null)
                {
                    return false;
                }

                var snapshot = loaded.Value;

                // 1. StreamId is preserved
                if (snapshot.StreamId != streamId)
                {
                    return false;
                }

                // 2. Version is preserved
                if (snapshot.Version != version)
                {
                    return false;
                }

                // 3. State data is preserved
                if (snapshot.State.Id != state.Id)
                {
                    return false;
                }

                if (snapshot.State.Name != state.Name)
                {
                    return false;
                }

                if (snapshot.State.Balance != state.Balance)
                {
                    return false;
                }

                if (snapshot.State.Status != state.Status)
                {
                    return false;
                }

                // 4. Timestamp is set (not default)
                if (snapshot.Timestamp == default)
                {
                    return false;
                }

                return true;
            });
    }

    /// <summary>
    /// Property 5 (Alternative): SnapshotStore Round-Trip with Multiple Saves
    /// 
    /// *For any* stream with multiple snapshots saved at different versions, 
    /// loading SHALL return the latest snapshot with correct data.
    /// 
    /// **Validates: Requirements 2.13**
    /// 
    /// Feature: tdd-validation, Property 5: SnapshotStore Round-Trip Consistency (Multiple Saves)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property SnapshotStore_MultipleSaves_LoadReturnsLatest()
    {
        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            SnapshotGenerators.TestAggregateStateArbitrary(),
            SnapshotGenerators.TestAggregateStateArbitrary(),
            (streamId, state1, state2) =>
            {
                // Arrange
                var store = new EnhancedInMemorySnapshotStore();
                var version1 = 1L;
                var version2 = 2L;

                // Act - Save two snapshots at different versions
                store.SaveAsync(streamId, state1, version1).AsTask().GetAwaiter().GetResult();
                store.SaveAsync(streamId, state2, version2).AsTask().GetAwaiter().GetResult();
                
                var loaded = store.LoadAsync<TestAggregateState>(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Should return the latest snapshot (version2)
                if (loaded == null)
                {
                    return false;
                }

                var snapshot = loaded.Value;

                // Should return the latest version
                if (snapshot.Version != version2)
                {
                    return false;
                }

                // Should return the latest state data
                if (snapshot.State.Id != state2.Id)
                {
                    return false;
                }

                if (snapshot.State.Name != state2.Name)
                {
                    return false;
                }

                if (snapshot.State.Balance != state2.Balance)
                {
                    return false;
                }

                return true;
            });
    }

    /// <summary>
    /// Property 5 (Data Integrity): SnapshotStore Preserves Complex State
    /// 
    /// *For any* aggregate state with collections and nested data, saving then loading 
    /// SHALL preserve all data including collections.
    /// 
    /// **Validates: Requirements 2.13**
    /// 
    /// Feature: tdd-validation, Property 5: SnapshotStore Round-Trip Consistency (Complex State)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property SnapshotStore_RoundTrip_PreservesComplexState()
    {
        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            SnapshotGenerators.SnapshotVersionArbitrary(),
            Gen.ListOf(Arb.Default.NonEmptyString().Generator.Select(s => s.Get))
                .Where(l => l.Count() <= 10)
                .ToArbitrary(),
            (streamId, version, items) =>
            {
                // Arrange
                var store = new EnhancedInMemorySnapshotStore();
                var state = new TestAggregateState
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Test Aggregate",
                    Balance = 1000,
                    Status = "Active",
                    Items = items.ToList()
                };

                // Act
                store.SaveAsync(streamId, state, version).AsTask().GetAwaiter().GetResult();
                var loaded = store.LoadAsync<TestAggregateState>(streamId).AsTask().GetAwaiter().GetResult();

                // Assert
                if (loaded == null)
                {
                    return false;
                }

                var snapshot = loaded.Value;

                // Verify collections are preserved
                if (snapshot.State.Items.Count != state.Items.Count)
                {
                    return false;
                }

                for (int i = 0; i < state.Items.Count; i++)
                {
                    if (snapshot.State.Items[i] != state.Items[i])
                    {
                        return false;
                    }
                }

                return true;
            });
    }

    /// <summary>
    /// Property 6: SnapshotStore Latest Version Only
    /// 
    /// *For any* aggregate with multiple snapshots saved at different versions, 
    /// loading SHALL return only the snapshot with the highest version.
    /// 
    /// **Validates: Requirements 2.14**
    /// 
    /// Feature: tdd-validation, Property 6: SnapshotStore Latest Version Only
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property SnapshotStore_Load_ReturnsLatestVersionOnly()
    {
        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            Gen.ListOf(Gen.Choose(1, 1000).Select(i => (long)i))
                .Where(l => l.Count() >= 2 && l.Count() <= 10)
                .Select(l => l.Distinct().OrderBy(v => v).ToList())
                .Where(l => l.Count >= 2)
                .ToArbitrary(),
            (streamId, versions) =>
            {
                // Arrange
                var store = new EnhancedInMemorySnapshotStore();
                var states = new Dictionary<long, TestAggregateState>();

                // Create unique states for each version
                foreach (var version in versions)
                {
                    var state = new TestAggregateState
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"State at version {version}",
                        Balance = (int)version * 100,
                        Status = "Active"
                    };
                    states[version] = state;
                }

                // Act - Save snapshots in random order (not necessarily ascending)
                var shuffledVersions = versions.OrderBy(_ => Guid.NewGuid()).ToList();
                foreach (var version in shuffledVersions)
                {
                    store.SaveAsync(streamId, states[version], version).AsTask().GetAwaiter().GetResult();
                }

                // Load the snapshot
                var loaded = store.LoadAsync<TestAggregateState>(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Should return the snapshot with the highest version
                if (loaded == null)
                {
                    return false;
                }

                var maxVersion = versions.Max();
                var expectedState = states[maxVersion];

                // 1. Version should be the maximum
                if (loaded.Value.Version != maxVersion)
                {
                    return false;
                }

                // 2. State should match the state saved at max version
                if (loaded.Value.State.Id != expectedState.Id)
                {
                    return false;
                }

                if (loaded.Value.State.Name != expectedState.Name)
                {
                    return false;
                }

                if (loaded.Value.State.Balance != expectedState.Balance)
                {
                    return false;
                }

                return true;
            });
    }

    /// <summary>
    /// Property 6 (Alternative): SnapshotStore Latest Version with Non-Sequential Versions
    /// 
    /// *For any* aggregate with snapshots saved at non-sequential versions (e.g., 1, 5, 10, 3),
    /// loading SHALL return the snapshot with the highest version regardless of save order.
    /// 
    /// **Validates: Requirements 2.14**
    /// 
    /// Feature: tdd-validation, Property 6: SnapshotStore Latest Version Only (Non-Sequential)
    /// </summary>
    [Property(MaxTest = PropertyTestConfig.DefaultMaxTest)]
    public Property SnapshotStore_Load_ReturnsLatestVersion_RegardlessOfSaveOrder()
    {
        return Prop.ForAll(
            SnapshotGenerators.AggregateIdArbitrary(),
            Gen.Three(SnapshotGenerators.TestAggregateStateArbitrary().Generator).ToArbitrary(),
            (streamId, states) =>
            {
                // Arrange
                var store = new EnhancedInMemorySnapshotStore();
                var (state1, state2, state3) = states;
                
                // Use non-sequential versions
                var version1 = 5L;
                var version2 = 2L;
                var version3 = 10L; // This is the highest

                // Act - Save in non-sequential order (5, 2, 10)
                store.SaveAsync(streamId, state1, version1).AsTask().GetAwaiter().GetResult();
                store.SaveAsync(streamId, state2, version2).AsTask().GetAwaiter().GetResult();
                store.SaveAsync(streamId, state3, version3).AsTask().GetAwaiter().GetResult();

                var loaded = store.LoadAsync<TestAggregateState>(streamId).AsTask().GetAwaiter().GetResult();

                // Assert - Should return version 10 (the highest)
                if (loaded == null)
                {
                    return false;
                }

                // Version should be 10 (the maximum)
                if (loaded.Value.Version != version3)
                {
                    return false;
                }

                // State should be state3 (saved at version 10)
                if (loaded.Value.State.Id != state3.Id)
                {
                    return false;
                }

                return true;
            });
    }
}
