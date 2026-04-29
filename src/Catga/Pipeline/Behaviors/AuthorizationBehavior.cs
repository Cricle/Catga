using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.Security;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that enforces [Authorize] on handlers.
/// Pure .NET — works in any host (worker, console, web).
/// Register via services.AddCatga().WithAuthorization().
/// </summary>
public sealed class AuthorizationBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ISecurityContext _security;
    private readonly IAuthorizationPolicyRegistry? _policies;

    // Cache attribute lookup per request type
    private static readonly AuthorizeAttribute? _authorizeAttr =
        typeof(TRequest).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .OfType<AuthorizeAttribute>().FirstOrDefault();

    private static readonly bool _allowAnonymous =
        typeof(TRequest).GetCustomAttributes(typeof(AllowAnonymousAttribute), true).Any();

    public AuthorizationBehavior(ISecurityContext security, IAuthorizationPolicyRegistry? policies = null)
    {
        _security = security;
        _policies = policies;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // No [Authorize] or [AllowAnonymous] → pass through
        if (_authorizeAttr == null || _allowAnonymous)
            return await next();

        var user = _security.User;

        // Must be authenticated
        if (user?.Identity?.IsAuthenticated != true)
            return CatgaResult<TResponse>.Failure(
                new ErrorInfo { Code = ErrorCodes.Unauthorized, Message = "Authentication required." });

        // Check roles
        if (_authorizeAttr.Roles?.Length > 0)
        {
            var hasRole = _authorizeAttr.Roles.Any(r => user.IsInRole(r));
            if (!hasRole)
                return CatgaResult<TResponse>.Failure(
                    new ErrorInfo { Code = ErrorCodes.Forbidden, Message = $"Required role: {string.Join(", ", _authorizeAttr.Roles)}" });
        }

        // Check named policy
        if (_authorizeAttr.Policy != null && _policies != null)
        {
            var policy = _policies.Get(_authorizeAttr.Policy);
            if (policy != null)
            {
                var allowed = await policy.AuthorizeAsync(user, request, cancellationToken);
                if (!allowed)
                    return CatgaResult<TResponse>.Failure(
                        new ErrorInfo { Code = ErrorCodes.Forbidden, Message = $"Policy '{_authorizeAttr.Policy}' denied." });
            }
        }

        return await next();
    }
}
