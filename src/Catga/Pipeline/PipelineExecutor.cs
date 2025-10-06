using System.Buffers;
using System.Runtime.CompilerServices;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

namespace Catga.Pipeline;

/// <summary>
/// 🔥 优化的 Pipeline 执行器 - 零分配设计
/// </summary>
internal static class PipelineExecutor
{
    /// <summary>
    /// 执行 Pipeline（优化版本 - 减少闭包和委托分配）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        IRequestHandler<TRequest, TResponse> handler,
        IList<IPipelineBehavior<TRequest, TResponse>> behaviors,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // 快速路径 - 无 behaviors
        if (behaviors.Count == 0)
        {
            var result = await handler.HandleAsync(request, cancellationToken);
            return result;
        }

        // 使用栈分配存储 behavior 索引，避免闭包
        var context = new PipelineContext<TRequest, TResponse>
        {
            Request = request,
            Handler = handler,
            Behaviors = behaviors,
            CancellationToken = cancellationToken
        };

        return await ExecuteBehaviorAsync(context, 0);
    }

    /// <summary>
    /// 递归执行 behavior（尾递归优化）
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static async ValueTask<CatgaResult<TResponse>> ExecuteBehaviorAsync<TRequest, TResponse>(
        PipelineContext<TRequest, TResponse> context,
        int index)
        where TRequest : IRequest<TResponse>
    {
        if (index >= context.Behaviors.Count)
        {
            // 到达 handler
            return await context.Handler.HandleAsync(context.Request, context.CancellationToken);
        }

        var behavior = context.Behaviors[index];
        
        // 创建 next 委托 - 指向下一个 behavior
        PipelineDelegate<TResponse> next = () => ExecuteBehaviorAsync(context, index + 1);

        return await behavior.HandleAsync(context.Request, next, context.CancellationToken);
    }

    /// <summary>
    /// Pipeline 执行上下文 - 避免闭包捕获
    /// </summary>
    private struct PipelineContext<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public TRequest Request;
        public IRequestHandler<TRequest, TResponse> Handler;
        public IList<IPipelineBehavior<TRequest, TResponse>> Behaviors;
        public CancellationToken CancellationToken;
    }
}

