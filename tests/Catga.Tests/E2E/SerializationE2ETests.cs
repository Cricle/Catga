using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// E2E tests for Serialization features.
/// Tests message serialization, deserialization, and format handling.
/// </summary>
public class SerializationE2ETests
{
    [Fact]
    public void Serializer_SerializeAndDeserialize_RoundTrip()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new TestMessage
        {
            Id = "MSG-001",
            Content = "Hello World",
            Timestamp = DateTime.UtcNow,
            Value = 123.45m
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<TestMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(original.Id);
        deserialized.Content.Should().Be(original.Content);
        deserialized.Value.Should().Be(original.Value);
    }

    [Fact]
    public void Serializer_ComplexObject_PreservesStructure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new ComplexOrder
        {
            OrderId = "ORD-001",
            Customer = new CustomerInfo
            {
                CustomerId = "CUST-001",
                Name = "John Doe",
                Email = "john@example.com"
            },
            Items = new List<OrderItem>
            {
                new OrderItem { ItemId = "ITEM-001", Quantity = 2, Price = 25.00m },
                new OrderItem { ItemId = "ITEM-002", Quantity = 1, Price = 50.00m }
            },
            TotalAmount = 100.00m,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<ComplexOrder>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.OrderId.Should().Be(original.OrderId);
        deserialized.Customer.Should().NotBeNull();
        deserialized.Customer.Name.Should().Be("John Doe");
        deserialized.Items.Should().HaveCount(2);
        deserialized.TotalAmount.Should().Be(100.00m);
    }

    [Fact]
    public void Serializer_NullValues_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new NullableMessage
        {
            Id = "MSG-001",
            OptionalValue = null,
            OptionalList = null
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<NullableMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("MSG-001");
        deserialized.OptionalValue.Should().BeNull();
        deserialized.OptionalList.Should().BeNull();
    }

    [Fact]
    public void Serializer_EmptyCollections_PreservedAsEmpty()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new CollectionMessage
        {
            Items = new List<string>(),
            Tags = new HashSet<string>(),
            Properties = new Dictionary<string, object>()
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<CollectionMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().BeEmpty();
        deserialized.Tags.Should().BeEmpty();
        deserialized.Properties.Should().BeEmpty();
    }

    [Fact]
    public void Serializer_NestedDictionary_Preserved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new DictionaryMessage
        {
            Data = new Dictionary<string, object>
            {
                ["string"] = "value",
                ["number"] = 42,
                ["decimal"] = 123.45m,
                ["boolean"] = true
            }
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<DictionaryMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Data.Should().ContainKey("string");
        deserialized.Data.Should().ContainKey("number");
        deserialized.Data.Should().ContainKey("boolean");
    }

    [Fact]
    public void Serializer_DateTimeFormats_PreservedCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var now = DateTime.UtcNow;
        var original = new DateTimeMessage
        {
            UtcDateTime = now,
            LocalDateTime = DateTime.Now,
            DateOnly = DateOnly.FromDateTime(now),
            TimeOnly = TimeOnly.FromDateTime(now)
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<DateTimeMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UtcDateTime.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Serializer_EnumValues_SerializedCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new EnumMessage
        {
            Status = OrderStatus.Shipped,
            Priority = Priority.High,
            Flags = ProcessingFlags.Urgent | ProcessingFlags.Express
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<EnumMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Status.Should().Be(OrderStatus.Shipped);
        deserialized.Priority.Should().Be(Priority.High);
    }

    [Fact]
    public void Serializer_LargePayload_HandledCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new LargeMessage
        {
            Id = "LARGE-001",
            Data = new string('X', 100_000), // 100KB string
            Items = Enumerable.Range(1, 1000).Select(i => new SimpleItem { Index = i, Value = $"Item-{i}" }).ToList()
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<LargeMessage>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Data.Length.Should().Be(100_000);
        deserialized.Items.Should().HaveCount(1000);
    }

    [Fact]
    public void Serializer_TypedDeserialize_ReturnsCorrectType()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new TestMessage { Id = "MSG-001", Content = "Test" };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize(bytes, typeof(TestMessage));

        // Assert
        deserialized.Should().NotBeNull();
        deserialized.Should().BeOfType<TestMessage>();
        ((TestMessage)deserialized!).Id.Should().Be("MSG-001");
    }

    [Fact]
    public void Serializer_PolymorphicTypes_HandledWithTypeInfo()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<IMessageSerializer>();

        var original = new PolymorphicContainer
        {
            BaseItems = new List<BaseItem>
            {
                new DerivedItemA { Name = "Item A", ValueA = 100 },
                new DerivedItemB { Name = "Item B", ValueB = "Text" }
            }
        };

        // Act
        var bytes = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<PolymorphicContainer>(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.BaseItems.Should().HaveCount(2);
    }

    #region Test Types

    public class TestMessage
    {
        public string Id { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public decimal Value { get; set; }
    }

    public class ComplexOrder
    {
        public string OrderId { get; set; } = "";
        public CustomerInfo Customer { get; set; } = new();
        public List<OrderItem> Items { get; set; } = new();
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CustomerInfo
    {
        public string CustomerId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }

    public class OrderItem
    {
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }
        public decimal Price { get; set; }
    }

    public class NullableMessage
    {
        public string Id { get; set; } = "";
        public string? OptionalValue { get; set; }
        public List<string>? OptionalList { get; set; }
    }

    public class CollectionMessage
    {
        public List<string> Items { get; set; } = new();
        public HashSet<string> Tags { get; set; } = new();
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    public class DictionaryMessage
    {
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class DateTimeMessage
    {
        public DateTime UtcDateTime { get; set; }
        public DateTime LocalDateTime { get; set; }
        public DateOnly DateOnly { get; set; }
        public TimeOnly TimeOnly { get; set; }
    }

    public class EnumMessage
    {
        public OrderStatus Status { get; set; }
        public Priority Priority { get; set; }
        public ProcessingFlags Flags { get; set; }
    }

    public enum OrderStatus { Created, Confirmed, Shipped, Delivered }
    public enum Priority { Low, Medium, High }
    [Flags] public enum ProcessingFlags { None = 0, Urgent = 1, Express = 2, Priority = 4 }

    public class LargeMessage
    {
        public string Id { get; set; } = "";
        public string Data { get; set; } = "";
        public List<SimpleItem> Items { get; set; } = new();
    }

    public class SimpleItem
    {
        public int Index { get; set; }
        public string Value { get; set; } = "";
    }

    public class PolymorphicContainer
    {
        public List<BaseItem> BaseItems { get; set; } = new();
    }

    public class BaseItem
    {
        public string Name { get; set; } = "";
    }

    public class DerivedItemA : BaseItem
    {
        public int ValueA { get; set; }
    }

    public class DerivedItemB : BaseItem
    {
        public string ValueB { get; set; } = "";
    }

    #endregion
}
