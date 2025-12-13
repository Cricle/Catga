using System.Text.Json;
using Microsoft.Data.Sqlite;
using OrderSystem.Api.Domain;
using OrderSystem.Api.Messages;

namespace OrderSystem.Api.Services;

/// <summary>
/// SQLite-based order repository - AOT compatible.
/// Stores orders in a local SQLite database file.
/// </summary>
public sealed class SqliteOrderRepository : IOrderRepository, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly Lock _lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SqliteOrderRepository(string connectionString = "Data Source=orders.db")
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Orders (
                OrderId TEXT PRIMARY KEY,
                CustomerId TEXT NOT NULL,
                Status INTEGER NOT NULL,
                TotalAmount REAL NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT,
                PaidAt TEXT,
                ShippedAt TEXT,
                DeliveredAt TEXT,
                CancelledAt TEXT,
                CancellationReason TEXT,
                TrackingNumber TEXT,
                PaymentMethod TEXT,
                PaymentTransactionId TEXT,
                ItemsJson TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_Orders_CustomerId ON Orders(CustomerId);
            CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status);
            CREATE INDEX IF NOT EXISTS IX_Orders_CreatedAt ON Orders(CreatedAt);
            """;
        cmd.ExecuteNonQuery();
    }

    public ValueTask<Order?> GetByIdAsync(string orderId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Orders WHERE OrderId = $orderId";
            cmd.Parameters.AddWithValue("$orderId", orderId);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return ValueTask.FromResult<Order?>(MapOrder(reader));
            }
            return ValueTask.FromResult<Order?>(null);
        }
    }

    public ValueTask<List<Order>> GetByCustomerIdAsync(string customerId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var orders = new List<Order>();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM Orders WHERE CustomerId = $customerId ORDER BY CreatedAt DESC";
            cmd.Parameters.AddWithValue("$customerId", customerId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                orders.Add(MapOrder(reader));
            }
            return ValueTask.FromResult(orders);
        }
    }

    public ValueTask<List<Order>> GetAllAsync(OrderStatus? status = null, int limit = 100, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var orders = new List<Order>();
            using var cmd = _connection.CreateCommand();

            if (status.HasValue)
            {
                cmd.CommandText = "SELECT * FROM Orders WHERE Status = $status ORDER BY CreatedAt DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$status", (int)status.Value);
            }
            else
            {
                cmd.CommandText = "SELECT * FROM Orders ORDER BY CreatedAt DESC LIMIT $limit";
            }
            cmd.Parameters.AddWithValue("$limit", limit);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                orders.Add(MapOrder(reader));
            }
            return ValueTask.FromResult(orders);
        }
    }

    public ValueTask<OrderStats> GetStatsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                SELECT
                    COUNT(*) as Total,
                    SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) as Pending,
                    SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) as Paid,
                    SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) as Processing,
                    SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) as Shipped,
                    SUM(CASE WHEN Status = 4 THEN 1 ELSE 0 END) as Delivered,
                    SUM(CASE WHEN Status = 5 THEN 1 ELSE 0 END) as Cancelled,
                    SUM(CASE WHEN Status = 4 THEN TotalAmount ELSE 0 END) as TotalRevenue
                FROM Orders
                """;

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return ValueTask.FromResult(new OrderStats(
                    Total: reader.GetInt32(0),
                    Pending: reader.GetInt32(1),
                    Paid: reader.GetInt32(2),
                    Processing: reader.GetInt32(3),
                    Shipped: reader.GetInt32(4),
                    Delivered: reader.GetInt32(5),
                    Cancelled: reader.GetInt32(6),
                    TotalRevenue: reader.IsDBNull(7) ? 0 : reader.GetDecimal(7)
                ));
            }
            return ValueTask.FromResult(new OrderStats(0, 0, 0, 0, 0, 0, 0, 0));
        }
    }

    public ValueTask SaveAsync(Order order, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO Orders (OrderId, CustomerId, Status, TotalAmount, CreatedAt, UpdatedAt,
                    PaidAt, ShippedAt, DeliveredAt, CancelledAt, CancellationReason,
                    TrackingNumber, PaymentMethod, PaymentTransactionId, ItemsJson)
                VALUES ($orderId, $customerId, $status, $totalAmount, $createdAt, $updatedAt,
                    $paidAt, $shippedAt, $deliveredAt, $cancelledAt, $cancellationReason,
                    $trackingNumber, $paymentMethod, $paymentTransactionId, $itemsJson)
                """;
            AddOrderParameters(cmd, order);
            cmd.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask UpdateAsync(Order order, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                UPDATE Orders SET
                    CustomerId = $customerId, Status = $status, TotalAmount = $totalAmount,
                    CreatedAt = $createdAt, UpdatedAt = $updatedAt, PaidAt = $paidAt,
                    ShippedAt = $shippedAt, DeliveredAt = $deliveredAt, CancelledAt = $cancelledAt,
                    CancellationReason = $cancellationReason, TrackingNumber = $trackingNumber,
                    PaymentMethod = $paymentMethod, PaymentTransactionId = $paymentTransactionId,
                    ItemsJson = $itemsJson
                WHERE OrderId = $orderId
                """;
            AddOrderParameters(cmd, order);
            cmd.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }

    public ValueTask DeleteAsync(string orderId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM Orders WHERE OrderId = $orderId";
            cmd.Parameters.AddWithValue("$orderId", orderId);
            cmd.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }

    private void AddOrderParameters(SqliteCommand cmd, Order order)
    {
        cmd.Parameters.AddWithValue("$orderId", order.OrderId);
        cmd.Parameters.AddWithValue("$customerId", order.CustomerId);
        cmd.Parameters.AddWithValue("$status", (int)order.Status);
        cmd.Parameters.AddWithValue("$totalAmount", order.TotalAmount);
        cmd.Parameters.AddWithValue("$createdAt", order.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$updatedAt", order.UpdatedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$paidAt", order.PaidAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$shippedAt", order.ShippedAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$deliveredAt", order.DeliveredAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$cancelledAt", order.CancelledAt?.ToString("O") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$cancellationReason", order.CancellationReason ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$trackingNumber", order.TrackingNumber ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$paymentMethod", order.PaymentMethod ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$paymentTransactionId", order.PaymentTransactionId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$itemsJson", JsonSerializer.Serialize(order.Items, JsonOptions));
    }

    private static Order MapOrder(SqliteDataReader reader)
    {
        var itemsJson = reader.GetString(reader.GetOrdinal("ItemsJson"));
        var items = JsonSerializer.Deserialize<List<OrderItem>>(itemsJson, JsonOptions) ?? [];

        return new Order
        {
            OrderId = reader.GetString(reader.GetOrdinal("OrderId")),
            CustomerId = reader.GetString(reader.GetOrdinal("CustomerId")),
            Status = (OrderStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            TotalAmount = reader.GetDecimal(reader.GetOrdinal("TotalAmount")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            UpdatedAt = GetNullableDateTime(reader, "UpdatedAt"),
            PaidAt = GetNullableDateTime(reader, "PaidAt"),
            ShippedAt = GetNullableDateTime(reader, "ShippedAt"),
            DeliveredAt = GetNullableDateTime(reader, "DeliveredAt"),
            CancelledAt = GetNullableDateTime(reader, "CancelledAt"),
            CancellationReason = GetNullableString(reader, "CancellationReason"),
            TrackingNumber = GetNullableString(reader, "TrackingNumber"),
            PaymentMethod = GetNullableString(reader, "PaymentMethod"),
            PaymentTransactionId = GetNullableString(reader, "PaymentTransactionId"),
            Items = items
        };
    }

    private static DateTime? GetNullableDateTime(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateTime.Parse(reader.GetString(ordinal));
    }

    private static string? GetNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public ValueTask DisposeAsync()
    {
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
