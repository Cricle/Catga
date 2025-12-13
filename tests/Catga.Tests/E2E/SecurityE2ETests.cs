using Catga.Abstractions;
using Catga.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Security E2E tests.
/// Tests authentication, authorization, data protection, and security patterns.
/// </summary>
public class SecurityE2ETests
{
    [Fact]
    public async Task Security_Unauthorized_RejectsRequest()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<SecureCommand, SecureResponse>, SecureHandler>();
        services.AddSingleton<IPipelineBehavior<SecureCommand, SecureResponse>, AuthorizationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new SecureCommand("action", null); // No token

        await Assert.ThrowsAsync<UnauthorizedException>(async () =>
        {
            await mediator.SendAsync(command);
        });
    }

    [Fact]
    public async Task Security_ValidToken_AllowsRequest()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<SecureCommand, SecureResponse>, SecureHandler>();
        services.AddSingleton<IPipelineBehavior<SecureCommand, SecureResponse>, AuthorizationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new SecureCommand("action", "valid-token-123");

        var response = await mediator.SendAsync(command);

        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Security_ExpiredToken_RejectsRequest()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<SecureCommand, SecureResponse>, SecureHandler>();
        services.AddSingleton<IPipelineBehavior<SecureCommand, SecureResponse>, TokenValidationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new SecureCommand("action", "expired-token");

        await Assert.ThrowsAsync<TokenExpiredException>(async () =>
        {
            await mediator.SendAsync(command);
        });
    }

    [Fact]
    public async Task Security_InsufficientPermissions_DeniesAccess()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<AdminCommand, AdminResponse>, AdminHandler>();
        services.AddSingleton<IPipelineBehavior<AdminCommand, AdminResponse>, RoleAuthorizationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new AdminCommand("delete-all", "user-role"); // Not admin

        await Assert.ThrowsAsync<ForbiddenException>(async () =>
        {
            await mediator.SendAsync(command);
        });
    }

    [Fact]
    public async Task Security_AdminRole_AllowsAdminAction()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<AdminCommand, AdminResponse>, AdminHandler>();
        services.AddSingleton<IPipelineBehavior<AdminCommand, AdminResponse>, RoleAuthorizationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var command = new AdminCommand("delete-all", "admin");

        var response = await mediator.SendAsync(command);

        response.Should().NotBeNull();
        response.Executed.Should().BeTrue();
    }

    [Fact]
    public void Security_SensitiveDataEncryption_ProtectsData()
    {
        var sensitiveData = "credit-card-1234-5678-9012-3456";
        var key = new byte[32]; // 256-bit key
        RandomNumberGenerator.Fill(key);

        var encrypted = EncryptData(sensitiveData, key);
        var decrypted = DecryptData(encrypted, key);

        encrypted.Should().NotBe(sensitiveData);
        decrypted.Should().Be(sensitiveData);
    }

    [Fact]
    public void Security_PasswordHashing_ProducesSecureHash()
    {
        var password = "SecureP@ssw0rd!";

        var hash1 = HashPassword(password);
        var hash2 = HashPassword(password);

        // Hashes should be different (due to salt)
        hash1.Should().NotBe(hash2);

        // But verification should work
        VerifyPassword(password, hash1).Should().BeTrue();
        VerifyPassword(password, hash2).Should().BeTrue();
        VerifyPassword("WrongPassword", hash1).Should().BeFalse();
    }

    [Fact]
    public async Task Security_RateLimiting_BlocksExcessiveRequests()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<RateLimitedCommand, RateLimitedResponse>, RateLimitedHandler>();
        services.AddSingleton<RateLimiter>();
        services.AddSingleton<IPipelineBehavior<RateLimitedCommand, RateLimitedResponse>, RateLimitBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var successCount = 0;
        var blockedCount = 0;

        // Try 20 requests, rate limit is 10
        for (int i = 0; i < 20; i++)
        {
            try
            {
                await mediator.SendAsync(new RateLimitedCommand("user-1"));
                successCount++;
            }
            catch (RateLimitExceededException)
            {
                blockedCount++;
            }
        }

        successCount.Should().BeLessOrEqualTo(10);
        blockedCount.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public async Task Security_InputValidation_RejectsInjection()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        services.AddSingleton<IRequestHandler<SearchCommand, SearchResponse>, SearchHandler>();
        services.AddSingleton<IPipelineBehavior<SearchCommand, SearchResponse>, InputSanitizationBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        var maliciousInput = "'; DROP TABLE users; --";
        var command = new SearchCommand(maliciousInput);

        await Assert.ThrowsAsync<InvalidInputException>(async () =>
        {
            await mediator.SendAsync(command);
        });
    }

    [Fact]
    public async Task Security_AuditLogging_RecordsSecurityEvents()
    {
        var services = new ServiceCollection();
        services.AddCatga(opt => opt.ForDevelopment())
            .UseInMemory();

        var auditLog = new List<AuditEntry>();
        services.AddSingleton(auditLog);
        services.AddSingleton<IRequestHandler<AuditedCommand, AuditedResponse>, AuditedHandler>();
        services.AddSingleton<IPipelineBehavior<AuditedCommand, AuditedResponse>, AuditBehavior>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        await mediator.SendAsync(new AuditedCommand("user-123", "ViewSensitiveData"));

        auditLog.Should().HaveCount(1);
        auditLog[0].UserId.Should().Be("user-123");
        auditLog[0].Action.Should().Be("ViewSensitiveData");
    }

    [Fact]
    public void Security_SecureRandomGeneration_ProducesUnpredictableValues()
    {
        var tokens = new HashSet<string>();

        for (int i = 0; i < 100; i++)
        {
            var token = GenerateSecureToken();
            tokens.Add(token);
        }

        // All tokens should be unique
        tokens.Should().HaveCount(100);
    }

    #region Helper Methods

    private static string EncryptData(string plainText, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    private static string DecryptData(string cipherText, byte[] key)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[aes.IV.Length];
        var cipher = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string HashPassword(string password)
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);

        var result = new byte[48];
        Buffer.BlockCopy(salt, 0, result, 0, 16);
        Buffer.BlockCopy(hash, 0, result, 16, 32);

        return Convert.ToBase64String(result);
    }

    private static bool VerifyPassword(string password, string hashedPassword)
    {
        var hashBytes = Convert.FromBase64String(hashedPassword);
        var salt = new byte[16];
        Buffer.BlockCopy(hashBytes, 0, salt, 0, 16);

        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(32);

        for (int i = 0; i < 32; i++)
        {
            if (hashBytes[i + 16] != hash[i]) return false;
        }

        return true;
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    #endregion

    #region Test Types

    public record SecureCommand(string Action, string? Token) : IRequest<SecureResponse>;
    public record SecureResponse(bool Success);
    public record AdminCommand(string Action, string Role) : IRequest<AdminResponse>;
    public record AdminResponse(bool Executed);
    public record RateLimitedCommand(string UserId) : IRequest<RateLimitedResponse>;
    public record RateLimitedResponse(bool Success);
    public record SearchCommand(string Query) : IRequest<SearchResponse>;
    public record SearchResponse(List<string> Results);
    public record AuditedCommand(string UserId, string Action) : IRequest<AuditedResponse>;
    public record AuditedResponse(bool Success);
    public record AuditEntry(string UserId, string Action, DateTime Timestamp);

    public class SecureHandler : IRequestHandler<SecureCommand, SecureResponse>
    {
        public ValueTask<SecureResponse> HandleAsync(SecureCommand request, CancellationToken ct = default)
            => ValueTask.FromResult(new SecureResponse(true));
    }

    public class AdminHandler : IRequestHandler<AdminCommand, AdminResponse>
    {
        public ValueTask<AdminResponse> HandleAsync(AdminCommand request, CancellationToken ct = default)
            => ValueTask.FromResult(new AdminResponse(true));
    }

    public class RateLimitedHandler : IRequestHandler<RateLimitedCommand, RateLimitedResponse>
    {
        public ValueTask<RateLimitedResponse> HandleAsync(RateLimitedCommand request, CancellationToken ct = default)
            => ValueTask.FromResult(new RateLimitedResponse(true));
    }

    public class SearchHandler : IRequestHandler<SearchCommand, SearchResponse>
    {
        public ValueTask<SearchResponse> HandleAsync(SearchCommand request, CancellationToken ct = default)
            => ValueTask.FromResult(new SearchResponse(new List<string> { "result1", "result2" }));
    }

    public class AuditedHandler : IRequestHandler<AuditedCommand, AuditedResponse>
    {
        public ValueTask<AuditedResponse> HandleAsync(AuditedCommand request, CancellationToken ct = default)
            => ValueTask.FromResult(new AuditedResponse(true));
    }

    public class AuthorizationBehavior : IPipelineBehavior<SecureCommand, SecureResponse>
    {
        public async ValueTask<SecureResponse> HandleAsync(SecureCommand request, RequestHandlerDelegate<SecureResponse> next, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(request.Token))
                throw new UnauthorizedException("Token required");
            return await next();
        }
    }

    public class TokenValidationBehavior : IPipelineBehavior<SecureCommand, SecureResponse>
    {
        public async ValueTask<SecureResponse> HandleAsync(SecureCommand request, RequestHandlerDelegate<SecureResponse> next, CancellationToken ct = default)
        {
            if (request.Token == "expired-token")
                throw new TokenExpiredException("Token has expired");
            return await next();
        }
    }

    public class RoleAuthorizationBehavior : IPipelineBehavior<AdminCommand, AdminResponse>
    {
        public async ValueTask<AdminResponse> HandleAsync(AdminCommand request, RequestHandlerDelegate<AdminResponse> next, CancellationToken ct = default)
        {
            if (request.Role != "admin")
                throw new ForbiddenException("Admin role required");
            return await next();
        }
    }

    public class RateLimiter
    {
        private readonly Dictionary<string, int> _counts = new();
        private readonly int _limit = 10;

        public bool TryAcquire(string userId)
        {
            if (!_counts.ContainsKey(userId)) _counts[userId] = 0;
            if (_counts[userId] >= _limit) return false;
            _counts[userId]++;
            return true;
        }
    }

    public class RateLimitBehavior : IPipelineBehavior<RateLimitedCommand, RateLimitedResponse>
    {
        private readonly RateLimiter _limiter;
        public RateLimitBehavior(RateLimiter limiter) => _limiter = limiter;

        public async ValueTask<RateLimitedResponse> HandleAsync(RateLimitedCommand request, RequestHandlerDelegate<RateLimitedResponse> next, CancellationToken ct = default)
        {
            if (!_limiter.TryAcquire(request.UserId))
                throw new RateLimitExceededException("Rate limit exceeded");
            return await next();
        }
    }

    public class InputSanitizationBehavior : IPipelineBehavior<SearchCommand, SearchResponse>
    {
        private static readonly string[] DangerousPatterns = { "DROP", "DELETE", "INSERT", "--", "'" };

        public async ValueTask<SearchResponse> HandleAsync(SearchCommand request, RequestHandlerDelegate<SearchResponse> next, CancellationToken ct = default)
        {
            if (DangerousPatterns.Any(p => request.Query.ToUpper().Contains(p)))
                throw new InvalidInputException("Potentially malicious input detected");
            return await next();
        }
    }

    public class AuditBehavior : IPipelineBehavior<AuditedCommand, AuditedResponse>
    {
        private readonly List<AuditEntry> _auditLog;
        public AuditBehavior(List<AuditEntry> auditLog) => _auditLog = auditLog;

        public async ValueTask<AuditedResponse> HandleAsync(AuditedCommand request, RequestHandlerDelegate<AuditedResponse> next, CancellationToken ct = default)
        {
            _auditLog.Add(new AuditEntry(request.UserId, request.Action, DateTime.UtcNow));
            return await next();
        }
    }

    public class UnauthorizedException : Exception { public UnauthorizedException(string msg) : base(msg) { } }
    public class TokenExpiredException : Exception { public TokenExpiredException(string msg) : base(msg) { } }
    public class ForbiddenException : Exception { public ForbiddenException(string msg) : base(msg) { } }
    public class RateLimitExceededException : Exception { public RateLimitExceededException(string msg) : base(msg) { } }
    public class InvalidInputException : Exception { public InvalidInputException(string msg) : base(msg) { } }

    #endregion
}
