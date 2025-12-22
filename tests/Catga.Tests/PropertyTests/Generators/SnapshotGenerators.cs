using Catga.Core;
using FsCheck;
using MemoryPack;

namespace Catga.Tests.PropertyTests.Generators;

/// <summary>
/// FsCheck 生成器 - 快照相关类型
/// 用于属性测试的快照数据生成
/// </summary>
public static class SnapshotGenerators
{
    /// <summary>
    /// 生成有效的聚合 ID
    /// </summary>
    public static Arbitrary<string> AggregateIdArbitrary()
    {
        return Gen.OneOf(
            // 常见格式
            Gen.Elements("order", "user", "product", "cart", "payment"),
            // 带 ID 的格式
            Gen.Fresh(() => $"aggregate-{Guid.NewGuid():N}"),
            // 带前缀的格式
            Gen.Elements("order-", "user-", "product-").SelectMany(prefix =>
                Gen.Choose(1, 10000).Select(id => $"{prefix}{id}"))
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成测试快照
    /// </summary>
    public static Arbitrary<TestSnapshot> TestSnapshotArbitrary()
    {
        return Arb.Default.NonEmptyString().Generator
            .SelectMany(name =>
                Gen.Choose(1, 10000).Select(value => new TestSnapshot
                {
                    Name = name.Get,
                    Value = value
                }))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成版本号
    /// </summary>
    public static Arbitrary<long> VersionArbitrary()
    {
        return Gen.Choose(0, 10000).Select(i => (long)i).ToArbitrary();
    }

    /// <summary>
    /// 生成测试聚合状态（用于其他测试文件）
    /// </summary>
    public static Arbitrary<TestAggregateState> TestAggregateStateArbitrary()
    {
        return Arb.Default.NonEmptyString().Generator
            .SelectMany(id =>
                Arb.Default.NonEmptyString().Generator.SelectMany(name =>
                    Gen.Elements("Active", "Inactive", "Pending", "Completed").Select(status =>
                        new TestAggregateState
                        {
                            Id = id.Get,
                            Name = name.Get,
                            Status = status
                        })))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成快照版本号（用于其他测试文件）
    /// </summary>
    public static Arbitrary<long> SnapshotVersionArbitrary()
    {
        return Gen.Choose(0, 10000).Select(i => (long)i).ToArbitrary();
    }
}

/// <summary>
/// 用于属性测试的测试快照
/// </summary>
[MemoryPackable]
public partial record TestSnapshot
{
    public required string Name { get; init; }
    public int Value { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 用于属性测试的测试聚合状态
/// </summary>
[MemoryPackable]
public partial class TestAggregateState
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public decimal Balance { get; init; }
    public List<string> Items { get; init; } = new();
}
