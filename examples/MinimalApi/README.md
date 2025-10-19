# Catga Minimal API Example

The simplest possible Catga application using .NET Minimal API.

## Features

- ✅ Command handling (CreateOrder)
- ✅ Event publishing (OrderCreated)
- ✅ InMemory transport
- ✅ Minimal API endpoints

## Run

```bash
dotnet run
```

## Test

```bash
# Create an order
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{"customerId":"customer-123","amount":99.99}'

# Get order status
curl http://localhost:5000/orders/{orderId}
```

## Expected Output

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "customerId": "customer-123",
  "amount": 99.99
}
```

Console log:
```
Creating order for customer customer-123
Order created: 3fa85f64-5717-4562-b3fc-2c963f66afa6, Customer: customer-123, Amount: 99.99
```

