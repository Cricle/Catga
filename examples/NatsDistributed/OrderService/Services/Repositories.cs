using OrderService.Models;

namespace OrderService.Services;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task<List<Order>> GetAllAsync(CancellationToken cancellationToken = default);
}

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(string productId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default);
}

public class InMemoryOrderRepository : IOrderRepository
{
    private readonly Dictionary<string, Order> _orders = new();
    private readonly object _lock = new();

    public Task<Order?> GetByIdAsync(string orderId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _orders.TryGetValue(orderId, out var order);
            return Task.FromResult(order);
        }
    }

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _orders[order.OrderId] = order;
        }
        return Task.CompletedTask;
    }

    public Task<List<Order>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_orders.Values.ToList());
        }
    }
}

public class InMemoryProductRepository : IProductRepository
{
    private readonly Dictionary<string, Product> _products;
    private readonly object _lock = new();

    public InMemoryProductRepository()
    {
        _products = new Dictionary<string, Product>
        {
            ["PROD-001"] = new Product { ProductId = "PROD-001", Name = "笔记本电脑", Price = 5999.99m, Stock = 10 },
            ["PROD-002"] = new Product { ProductId = "PROD-002", Name = "无线鼠标", Price = 199.99m, Stock = 50 },
            ["PROD-003"] = new Product { ProductId = "PROD-003", Name = "机械键盘", Price = 699.99m, Stock = 25 },
            ["PROD-004"] = new Product { ProductId = "PROD-004", Name = "显示器", Price = 2199.99m, Stock = 15 },
            ["PROD-005"] = new Product { ProductId = "PROD-005", Name = "网络摄像头", Price = 299.99m, Stock = 30 }
        };
    }

    public Task<Product?> GetByIdAsync(string productId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _products.TryGetValue(productId, out var product);
            return Task.FromResult(product);
        }
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _products[product.ProductId] = product;
        }
        return Task.CompletedTask;
    }

    public Task<List<Product>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_products.Values.ToList());
        }
    }
}
