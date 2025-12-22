using FsCheck;

namespace Catga.Tests.PropertyTests;

/// <summary>
/// FsCheck 属性测试配置类
/// 提供统一的属性测试配置和自定义生成器注册
/// 
/// 使用方式:
/// [Property(MaxTest = 100, Arbitrary = new[] { typeof(CatgaArbitraries) })]
/// public Property MyPropertyTest(string input) => ...
/// </summary>
public static class PropertyTestConfig
{
    /// <summary>
    /// 默认测试次数 - 每个属性测试运行 100 次迭代
    /// </summary>
    public const int DefaultMaxTest = 100;

    /// <summary>
    /// 快速测试次数 - 用于开发时快速验证，运行 20 次迭代
    /// </summary>
    public const int QuickMaxTest = 20;

    /// <summary>
    /// 详尽测试次数 - 用于 CI/CD 环境，运行 500 次迭代
    /// </summary>
    public const int ExhaustiveMaxTest = 500;

    /// <summary>
    /// 并发测试次数 - 较少迭代但更大的数据规模
    /// </summary>
    public const int ConcurrentMaxTest = 50;

    /// <summary>
    /// 默认起始大小
    /// </summary>
    public const int DefaultStartSize = 1;

    /// <summary>
    /// 默认结束大小
    /// </summary>
    public const int DefaultEndSize = 100;

    /// <summary>
    /// 注册所有自定义生成器到 FsCheck
    /// 在测试类的静态构造函数中调用此方法
    /// </summary>
    public static void RegisterGenerators()
    {
        Arb.Register<CatgaArbitraries>();
    }
}

/// <summary>
/// Catga 框架自定义 Arbitrary 生成器集合
/// 用于 FsCheck 属性测试的数据生成
/// 
/// 使用方式:
/// [Property(Arbitrary = new[] { typeof(CatgaArbitraries) })]
/// </summary>
public class CatgaArbitraries
{
    /// <summary>
    /// 生成非空字符串
    /// </summary>
    public static Arbitrary<NonEmptyString> NonEmptyStringArb()
    {
        return Arb.Default.NonEmptyString();
    }

    /// <summary>
    /// 生成正整数版本号
    /// </summary>
    public static Arbitrary<PositiveInt> PositiveVersionArb()
    {
        return Arb.Default.PositiveInt();
    }

    /// <summary>
    /// 生成有效的 GUID
    /// </summary>
    public static Arbitrary<Guid> GuidArb()
    {
        return Gen.Fresh(() => Guid.NewGuid()).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的流 ID（非空、非空白字符串）
    /// </summary>
    public static Arbitrary<string> StreamIdArb()
    {
        return Gen.Elements(
            "stream-1", "stream-2", "orders", "users", "events",
            "aggregate-123", "test-stream", "my-stream"
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的主题名称
    /// </summary>
    public static Arbitrary<string> TopicArb()
    {
        return Gen.Elements(
            "topic.events", "topic.commands", "orders.created",
            "users.updated", "system.notifications"
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的字节数组（用于事件数据）
    /// </summary>
    public static Arbitrary<byte[]> PayloadArb()
    {
        return Gen.Choose(1, 1000)
            .SelectMany(size => Gen.ArrayOf(size, Arb.Generate<byte>()))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成有效的时间戳
    /// </summary>
    public static Arbitrary<DateTime> TimestampArb()
    {
        return Gen.Choose(0, 365 * 10) // 过去10年内的天数
            .Select(days => DateTime.UtcNow.AddDays(-days))
            .ToArbitrary();
    }
}
