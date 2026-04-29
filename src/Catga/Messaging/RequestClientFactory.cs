using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Transport;

namespace Catga.Messaging;

/// <summary>
/// Default implementation of IRequestClientFactory.
/// Creates request clients backed by the registered IMessageTransport.
/// </summary>
public sealed class RequestClientFactory : IRequestClientFactory
{
    private readonly IMessageTransport _transport;
    private readonly TimeSpan _defaultTimeout;

    public RequestClientFactory(IMessageTransport transport, TimeSpan? defaultTimeout = null)
    {
        _transport = transport;
        _defaultTimeout = defaultTimeout
            ?? (transport as IRequestTimeoutDefaults)?.DefaultRequestTimeout
            ?? TimeSpan.FromSeconds(30);
    }

    public IRequestClient<TRequest, TResponse> CreateClient<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(
        string? destination = null,
        TimeSpan? defaultTimeout = null)
        where TRequest : class, IRequest<TResponse>
        where TResponse : class
    {
        var dest = destination ?? TypeNameCache<TRequest>.Name;
        return new TransportRequestClient<TRequest, TResponse>(
            _transport, dest, defaultTimeout ?? _defaultTimeout);
    }
}

/// <summary>
/// Request client backed by IMessageTransport.
/// </summary>
internal sealed class TransportRequestClient<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IRequestClient<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
    where TResponse : class
{
    private readonly IMessageTransport _transport;
    private readonly string _destination;
    private readonly TimeSpan _defaultTimeout;

    public TransportRequestClient(IMessageTransport transport, string destination, TimeSpan defaultTimeout)
    {
        _transport = transport;
        _destination = destination;
        _defaultTimeout = defaultTimeout;
    }

    public async Task<CatgaResult<TResponse>> RequestAsync(TRequest request, CancellationToken ct = default)
        => await RequestAsync(request, _defaultTimeout, ct);

    public async Task<CatgaResult<TResponse>> RequestAsync(TRequest request, TimeSpan timeout, CancellationToken ct = default)
    {
        try
        {
            var response = await _transport.RequestAsync<TRequest, TResponse>(
                request, _destination, timeout, ct);

            return response != null
                ? CatgaResult<TResponse>.Success(response)
                : CatgaResult<TResponse>.Failure(ErrorInfo.Timeout("No response received (timeout)"));
        }
        catch (OperationCanceledException)
        {
            return CatgaResult<TResponse>.Failure(new ErrorInfo { Code = ErrorCodes.Cancelled, Message = "Request cancelled" });
        }
        catch (Exception ex)
        {
            return CatgaResult<TResponse>.Failure(ErrorInfo.FromException(ex, ErrorCodes.TransportFailed));
        }
    }
}
