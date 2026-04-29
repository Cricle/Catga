using System.Security.Claims;
using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.DistributedId;
using Catga.EventSourcing;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Security;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Core;

// ── Domain types ──────────────────────────────────────────────────────────────

[Authorize]
public record SecureCommand(string Data) : IRequest<string> { public long MessageId { get; init; } }

[Authorize("admin")]
public record AdminCommand(string Data) : IRequest<string> { public long MessageId { get; init; } }

[Authorize(Policy = "premium")]
public record PremiumCommand(string Data) : IRequest<string> { public long MessageId { get; init; } }

[AllowAnonymous]
public record PublicCommand(string Data) : IRequest<string> { public long MessageId { get; init; } }

public record UnsecuredCommand(string Data) : IRequest<string> { public long MessageId { get; init; } }

// ═══════════════════════════════════════════════════════════════════════════════
// ISecurityContext TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class SecurityContextTests
{
    [Fact]
    public void SecurityContext_DefaultUser_IsNull()
    {
        var ctx = new SecurityContext();
        ctx.User.Should().BeNull();
        ((ISecurityContext)ctx).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void SecurityContext_SetUser_IsAuthenticated()
    {
        var ctx = new SecurityContext();
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Name, "alice"));
        ctx.SetUser(new ClaimsPrincipal(identity));

        ((ISecurityContext)ctx).IsAuthenticated.Should().BeTrue();
        ctx.User!.Identity!.Name.Should().Be("alice");
    }

    [Fact]
    public void SecurityContext_ClearUser_IsNotAuthenticated()
    {
        var ctx = new SecurityContext();
        ctx.SetUser(new ClaimsPrincipal(new ClaimsIdentity("test")));
        ctx.SetUser(null);
        ((ISecurityContext)ctx).IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task SecurityContext_AsyncLocal_IsolatedPerChain()
    {
        var ctx = new SecurityContext();
        string? name1 = null, name2 = null;

        await Task.WhenAll(
            Task.Run(() =>
            {
                var id = new ClaimsIdentity("t");
                id.AddClaim(new Claim(ClaimTypes.Name, "alice"));
                ctx.SetUser(new ClaimsPrincipal(id));
                name1 = ctx.User?.Identity?.Name;
            }),
            Task.Run(() =>
            {
                var id = new ClaimsIdentity("t");
                id.AddClaim(new Claim(ClaimTypes.Name, "bob"));
                ctx.SetUser(new ClaimsPrincipal(id));
                name2 = ctx.User?.Identity?.Name;
            }));

        name1.Should().Be("alice");
        name2.Should().Be("bob");
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// AuthorizationBehavior TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class AuthorizationBehaviorTests
{
    private static void ClearCaches()
    {
        var t = typeof(CatgaMediator);
        (t.GetField("_handlerCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
        (t.GetField("_behaviorCache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IDictionary)?.Clear();
    }

    private static ServiceProvider BuildSp(ClaimsPrincipal? user = null,
        Action<AuthorizationPolicyRegistry>? policies = null)
    {
        ClearCaches();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CatgaOptions>();
        services.AddSingleton<IDistributedIdGenerator, SnowflakeIdGenerator>();
        services.AddSingleton<IEventTypeRegistry, DefaultEventTypeRegistry>();
        services.AddSingleton<ISecurityContext>(new SecurityContext());
        services.AddSingleton<IAuthorizationPolicyRegistry>(sp =>
        {
            var reg = new AuthorizationPolicyRegistry();
            policies?.Invoke(reg);
            return reg;
        });
        services.AddSingleton<ICatgaMediator, CatgaMediator>();
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehavior<,>));

        // Register handlers
        services.AddSingleton<IRequestHandler<SecureCommand, string>>(
            _ => new EchoHandler<SecureCommand>());
        services.AddSingleton<IRequestHandler<AdminCommand, string>>(
            _ => new EchoHandler<AdminCommand>());
        services.AddSingleton<IRequestHandler<PremiumCommand, string>>(
            _ => new EchoHandler<PremiumCommand>());
        services.AddSingleton<IRequestHandler<PublicCommand, string>>(
            _ => new EchoHandler<PublicCommand>());
        services.AddSingleton<IRequestHandler<UnsecuredCommand, string>>(
            _ => new EchoHandler<UnsecuredCommand>());

        var sp = services.BuildServiceProvider();

        if (user != null)
            sp.GetRequiredService<ISecurityContext>().SetUser(user);

        return sp;
    }

    private static ClaimsPrincipal MakeUser(string name, params string[] roles)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new Claim(ClaimTypes.Name, name));
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task Authorize_NoUser_ReturnsUnauthorized()
    {
        await using var sp = BuildSp(user: null);
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<SecureCommand, string>(new SecureCommand("x"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Authorize_AuthenticatedUser_Succeeds()
    {
        await using var sp = BuildSp(user: MakeUser("alice"));
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<SecureCommand, string>(new SecureCommand("x"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Authorize_WithRole_WrongRole_ReturnsForbidden()
    {
        await using var sp = BuildSp(user: MakeUser("alice", "user"));
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<AdminCommand, string>(new AdminCommand("x"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Authorize_WithRole_CorrectRole_Succeeds()
    {
        await using var sp = BuildSp(user: MakeUser("alice", "admin"));
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<AdminCommand, string>(new AdminCommand("x"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AllowAnonymous_NoUser_Succeeds()
    {
        await using var sp = BuildSp(user: null);
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<PublicCommand, string>(new PublicCommand("x"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NoAuthorizeAttribute_NoUser_Succeeds()
    {
        await using var sp = BuildSp(user: null);
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<UnsecuredCommand, string>(new UnsecuredCommand("x"));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Authorize_WithPolicy_PolicyDenies_ReturnsForbidden()
    {
        await using var sp = BuildSp(
            user: MakeUser("alice"),
            policies: reg => reg.Register(new DenyAllPolicy("premium")));
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<PremiumCommand, string>(new PremiumCommand("x"));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
    }

    [Fact]
    public async Task Authorize_WithPolicy_PolicyAllows_Succeeds()
    {
        await using var sp = BuildSp(
            user: MakeUser("alice"),
            policies: reg => reg.Register(new AllowAllPolicy("premium")));
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var result = await mediator.SendAsync<PremiumCommand, string>(new PremiumCommand("x"));

        result.IsSuccess.Should().BeTrue();
    }

    private sealed class EchoHandler<T> : IRequestHandler<T, string> where T : IRequest<string>
    {
        public ValueTask<CatgaResult<string>> HandleAsync(T request, CancellationToken ct = default)
            => ValueTask.FromResult(CatgaResult<string>.Success("ok"));
    }

    private sealed class DenyAllPolicy : IAuthorizationPolicy
    {
        public string Name { get; }
        public DenyAllPolicy(string name) => Name = name;
        public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, object? resource = null, CancellationToken ct = default)
            => ValueTask.FromResult(false);
    }

    private sealed class AllowAllPolicy : IAuthorizationPolicy
    {
        public string Name { get; }
        public AllowAllPolicy(string name) => Name = name;
        public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, object? resource = null, CancellationToken ct = default)
            => ValueTask.FromResult(true);
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// HmacMessageSigner TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class HmacMessageSignerTests
{
    [Fact]
    public void Sign_ProducesNonEmptySignature()
    {
        var signer = new HmacMessageSigner("secret-key");
        var sig = signer.Sign(new byte[] { 1, 2, 3 });
        sig.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        var signer = new HmacMessageSigner("secret-key");
        var payload = System.Text.Encoding.UTF8.GetBytes("hello world");
        var sig = signer.Sign(payload);
        signer.Verify(payload, sig).Should().BeTrue();
    }

    [Fact]
    public void Verify_TamperedPayload_ReturnsFalse()
    {
        var signer = new HmacMessageSigner("secret-key");
        var payload = System.Text.Encoding.UTF8.GetBytes("hello world");
        var sig = signer.Sign(payload);
        var tampered = System.Text.Encoding.UTF8.GetBytes("hello WORLD");
        signer.Verify(tampered, sig).Should().BeFalse();
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        var signer1 = new HmacMessageSigner("key-1");
        var signer2 = new HmacMessageSigner("key-2");
        var payload = System.Text.Encoding.UTF8.GetBytes("data");
        var sig = signer1.Sign(payload);
        signer2.Verify(payload, sig).Should().BeFalse();
    }

    [Fact]
    public void Sign_SamePayload_SameSignature()
    {
        var signer = new HmacMessageSigner("key");
        var payload = new byte[] { 1, 2, 3 };
        signer.Sign(payload).Should().Be(signer.Sign(payload));
    }

    [Fact]
    public void Sign_DifferentPayloads_DifferentSignatures()
    {
        var signer = new HmacMessageSigner("key");
        signer.Sign(new byte[] { 1 }).Should().NotBe(signer.Sign(new byte[] { 2 }));
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// DI REGISTRATION TESTS
// ═══════════════════════════════════════════════════════════════════════════════

public class SecurityDiTests
{
    [Fact]
    public void WithAuthorization_RegistersISecurityContext()
    {
        var services = new ServiceCollection();
        services.AddCatga().WithAuthorization();
        var sp = services.BuildServiceProvider();
        sp.GetService<ISecurityContext>().Should().NotBeNull();
    }

    [Fact]
    public void WithAuthorization_RegistersIAuthorizationPolicyRegistry()
    {
        var services = new ServiceCollection();
        services.AddCatga().WithAuthorization();
        var sp = services.BuildServiceProvider();
        sp.GetService<IAuthorizationPolicyRegistry>().Should().NotBeNull();
    }

    [Fact]
    public void WithAuthorization_WithPolicies_PoliciesRegistered()
    {
        var services = new ServiceCollection();
        services.AddCatga().WithAuthorization(reg =>
            reg.Register(new LambdaPolicy("admin-only", (u, _) => u.IsInRole("admin"))));

        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAuthorizationPolicyRegistry>();
        registry.Get("admin-only").Should().NotBeNull();
    }

    [Fact]
    public void WithMessageSigning_RegistersIMessageSigner()
    {
        var services = new ServiceCollection();
        services.AddCatga().WithMessageSigning("my-secret");
        var sp = services.BuildServiceProvider();
        sp.GetService<IMessageSigner>().Should().NotBeNull();
    }

    [Fact]
    public void WithMessageSigning_SignerWorks()
    {
        var services = new ServiceCollection();
        services.AddCatga().WithMessageSigning("my-secret");
        var sp = services.BuildServiceProvider();
        var signer = sp.GetRequiredService<IMessageSigner>();
        var payload = new byte[] { 1, 2, 3 };
        signer.Verify(payload, signer.Sign(payload)).Should().BeTrue();
    }

    private sealed class LambdaPolicy : IAuthorizationPolicy
    {
        private readonly Func<ClaimsPrincipal, object?, bool> _check;
        public string Name { get; }
        public LambdaPolicy(string name, Func<ClaimsPrincipal, object?, bool> check)
        { Name = name; _check = check; }
        public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, object? resource = null, CancellationToken ct = default)
            => ValueTask.FromResult(_check(user, resource));
    }
}
