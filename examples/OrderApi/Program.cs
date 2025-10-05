using Catga.DependencyInjection;
using Catga.Handlers;
using OrderApi.Commands;
using OrderApi.Handlers;
using OrderApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 添加 Catga
builder.Services.AddCatga();

// 注册应用服务
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();

// 注册处理器
builder.Services.AddScoped<IRequestHandler<CreateOrderCommand, CreateOrderResult>, CreateOrderHandler>();
builder.Services.AddScoped<IRequestHandler<GetOrderQuery, OrderDto>, GetOrderQueryHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
