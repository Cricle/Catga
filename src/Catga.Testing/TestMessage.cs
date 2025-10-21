using Catga.Abstractions;
using Catga.Core;

namespace Catga.Testing;

/// <summary>
/// 测试用命令基类
/// </summary>
public record TestCommand : IRequest<TestResponse>
{
    public long MessageId { get; set; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtMostOnce;
}

/// <summary>
/// 测试用查询基类
/// </summary>
public record TestQuery : IRequest<TestResponse>
{
    public long MessageId { get; set; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtMostOnce;
}

/// <summary>
/// 测试用响应基类
/// </summary>
public record TestResponse
{
    public bool Success { get; init; } = true;
    public string? Message { get; init; }
    public object? Data { get; init; }

    public static TestResponse Ok(string? message = null, object? data = null) =>
        new() { Success = true, Message = message, Data = data };

    public static TestResponse Fail(string message) =>
        new() { Success = false, Message = message };
}

/// <summary>
/// 测试用事件基类
/// </summary>
public record TestEvent : IEvent
{
    public long MessageId { get; set; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtMostOnce;
    public string? EventType { get; init; }
    public object? Data { get; init; }
}

/// <summary>
/// 简单的测试命令
/// </summary>
public record SimpleTestCommand(string Name) : IRequest<string>
{
    public long MessageId { get; set; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtMostOnce;
}

/// <summary>
/// 简单的测试事件
/// </summary>
public record SimpleTestEvent(string Name, string Data) : IEvent
{
    public long MessageId { get; set; } = MessageExtensions.NewMessageId();
    public long? CorrelationId { get; set; }
    public QualityOfService QoS { get; set; } = QualityOfService.AtMostOnce;
}

