using System.Security.Claims;
using System.Security.Principal;

namespace Catga.Security;

// ── Attributes ────────────────────────────────────────────────────────────────

/// <summary>
/// Marks a handler as requiring authentication.
/// Works with any .NET host (console, worker, web, etc.).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AuthorizeAttribute : Attribute
{
    /// <summary>Required roles (any match grants access). Null = authenticated only.</summary>
    public string[]? Roles { get; set; }

    /// <summary>Required policy name. Evaluated via IAuthorizationPolicy.</summary>
    public string? Policy { get; set; }

    public AuthorizeAttribute() { }
    public AuthorizeAttribute(params string[] roles) => Roles = roles;
}

/// <summary>
/// Explicitly allows anonymous access even if a parent scope requires authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowAnonymousAttribute : Attribute { }

// ── Security context ──────────────────────────────────────────────────────────

/// <summary>
/// Ambient security context for the current execution scope.
/// Pure .NET — no dependency on ASP.NET Core or HTTP.
/// Set by your host (worker, web, console) before dispatching messages.
/// </summary>
public interface ISecurityContext
{
    /// <summary>Current principal. Null if not authenticated.</summary>
    ClaimsPrincipal? User { get; }

    /// <summary>Whether the current user is authenticated.</summary>
    bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    /// <summary>Set the current principal for this scope.</summary>
    void SetUser(ClaimsPrincipal? user);
}

/// <summary>
/// Default scoped security context using AsyncLocal for isolation.
/// </summary>
public sealed class SecurityContext : ISecurityContext
{
    private static readonly AsyncLocal<ClaimsPrincipal?> _user = new();

    public ClaimsPrincipal? User => _user.Value;
    public void SetUser(ClaimsPrincipal? user) => _user.Value = user;
}

// ── Authorization policy ──────────────────────────────────────────────────────

/// <summary>
/// Extensible authorization policy.
/// Implement to add custom authorization logic (e.g., resource-based, tenant-based).
/// </summary>
public interface IAuthorizationPolicy
{
    string Name { get; }
    ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, object? resource = null, CancellationToken ct = default);
}

/// <summary>
/// Registry of named authorization policies.
/// </summary>
public interface IAuthorizationPolicyRegistry
{
    void Register(IAuthorizationPolicy policy);
    IAuthorizationPolicy? Get(string name);
}

public sealed class AuthorizationPolicyRegistry : IAuthorizationPolicyRegistry
{
    private readonly Dictionary<string, IAuthorizationPolicy> _policies = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IAuthorizationPolicy policy) => _policies[policy.Name] = policy;
    public IAuthorizationPolicy? Get(string name)
        => _policies.TryGetValue(name, out var p) ? p : null;
}
