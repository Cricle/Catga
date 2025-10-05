namespace OrderApi.Services;

public class Order
{
    public string OrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Created";
    public DateTime CreatedAt { get; set; }
}

public class Product
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
}

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
}

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string productId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
}

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<string, Order> _orders = new();

    public Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.OrderId] = order;
        return Task.CompletedTask;
    }
}

public class InMemoryProductRepository : IProductRepository
{
    private readonly Dictionary<string, Product> _products = new()
    {
        ["PROD-001"] = new Product { ProductId = "PROD-001", Name = "Laptop", Price = 999.99m, Stock = 10 },
        ["PROD-002"] = new Product { ProductId = "PROD-002", Name = "Mouse", Price = 29.99m, Stock = 50 },
        ["PROD-003"] = new Product { ProductId = "PROD-003", Name = "Keyboard", Price = 79.99m, Stock = 25 }
    };

    public Task<Product?> GetByIdAsync(string productId, CancellationToken cancellationToken = default)
    {
        _products.TryGetValue(productId, out var product);
        return Task.FromResult(product);
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        _products[product.ProductId] = product;
        return Task.CompletedTask;
    }
}
