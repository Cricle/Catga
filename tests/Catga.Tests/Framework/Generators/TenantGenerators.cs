namespace Catga.Tests.Framework.Generators;

/// <summary>
/// 租户上下文
/// </summary>
public record TenantContext(
    string TenantId,
    string TenantName,
    Dictionary<string, object> Configuration,
    QuotaLimits Limits);

/// <summary>
/// 配额限制
/// </summary>
public record QuotaLimits(
    int MaxOrders,
    int MaxEventsPerSecond,
    long MaxStorageBytes);

/// <summary>
/// 租户数据生成器
/// </summary>
public static class TenantGenerators
{
    private static readonly Random Random = new();

    /// <summary>
    /// 生成单个租户
    /// </summary>
    public static TenantContext GenerateTenant(string? tenantId = null)
    {
        var id = tenantId ?? Guid.NewGuid().ToString();
        return new TenantContext(
            TenantId: id,
            TenantName: $"Tenant-{id[..8]}",
            Configuration: GenerateConfiguration(),
            Limits: GenerateQuotaLimits());
    }

    /// <summary>
    /// 生成租户对
    /// </summary>
    public static (TenantContext TenantA, TenantContext TenantB) GenerateTenantPair()
    {
        return (GenerateTenant(), GenerateTenant());
    }

    /// <summary>
    /// 生成多个租户
    /// </summary>
    public static List<TenantContext> GenerateTenants(int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => GenerateTenant())
            .ToList();
    }

    /// <summary>
    /// 生成租户配置
    /// </summary>
    private static Dictionary<string, object> GenerateConfiguration()
    {
        return new Dictionary<string, object>
        {
            ["Region"] = Random.Next(0, 3) switch
            {
                0 => "US-East",
                1 => "US-West",
                _ => "EU-Central"
            },
            ["Tier"] = Random.Next(0, 3) switch
            {
                0 => "Free",
                1 => "Pro",
                _ => "Enterprise"
            },
            ["Features"] = new List<string>
            {
                "Orders",
                "Payments",
                Random.Next(0, 2) == 0 ? "Analytics" : "Reporting"
            }
        };
    }

    /// <summary>
    /// 生成配额限制
    /// </summary>
    private static QuotaLimits GenerateQuotaLimits()
    {
        var tier = Random.Next(0, 3);
        return tier switch
        {
            0 => new QuotaLimits(
                MaxOrders: 100,
                MaxEventsPerSecond: 10,
                MaxStorageBytes: 100 * 1024 * 1024), // 100 MB
            1 => new QuotaLimits(
                MaxOrders: 1000,
                MaxEventsPerSecond: 100,
                MaxStorageBytes: 1024 * 1024 * 1024), // 1 GB
            _ => new QuotaLimits(
                MaxOrders: 10000,
                MaxEventsPerSecond: 1000,
                MaxStorageBytes: 10L * 1024 * 1024 * 1024) // 10 GB
        };
    }

    /// <summary>
    /// 生成租户操作
    /// </summary>
    public static TenantOperation GenerateTenantOperation(TenantContext? tenant = null)
    {
        tenant ??= GenerateTenant();
        
        var operationType = Random.Next(0, 4) switch
        {
            0 => OperationType.CreateOrder,
            1 => OperationType.UpdateOrder,
            2 => OperationType.QueryOrders,
            _ => OperationType.DeleteOrder
        };

        return new TenantOperation(
            TenantId: tenant.TenantId,
            OperationType: operationType,
            Data: GenerateOperationData(operationType));
    }

    /// <summary>
    /// 生成操作数据
    /// </summary>
    private static Dictionary<string, object> GenerateOperationData(OperationType operationType)
    {
        return operationType switch
        {
            OperationType.CreateOrder => new Dictionary<string, object>
            {
                ["OrderId"] = Guid.NewGuid().ToString(),
                ["Amount"] = Random.Next(10, 1000),
                ["Items"] = Random.Next(1, 10)
            },
            OperationType.UpdateOrder => new Dictionary<string, object>
            {
                ["OrderId"] = Guid.NewGuid().ToString(),
                ["Status"] = Random.Next(0, 3) switch
                {
                    0 => "Pending",
                    1 => "Paid",
                    _ => "Shipped"
                }
            },
            OperationType.QueryOrders => new Dictionary<string, object>
            {
                ["Limit"] = Random.Next(10, 100),
                ["Offset"] = Random.Next(0, 1000)
            },
            OperationType.DeleteOrder => new Dictionary<string, object>
            {
                ["OrderId"] = Guid.NewGuid().ToString()
            },
            _ => new Dictionary<string, object>()
        };
    }
}

/// <summary>
/// 租户操作
/// </summary>
public record TenantOperation(
    string TenantId,
    OperationType OperationType,
    Dictionary<string, object> Data);

/// <summary>
/// 操作类型
/// </summary>
public enum OperationType
{
    CreateOrder,
    UpdateOrder,
    QueryOrders,
    DeleteOrder
}
