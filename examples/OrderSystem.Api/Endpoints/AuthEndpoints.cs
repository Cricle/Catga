using OrderSystem.Api.Domain;

namespace OrderSystem.Api.Endpoints;

/// <summary>
/// Authentication endpoints for user login and registration
/// </summary>
public static class AuthEndpoints
{
    // In-memory user store (replace with database in production)
    private static readonly Dictionary<string, User> Users = new();

    static AuthEndpoints()
    {
        // Seed default users
        var adminUser = new User
        {
            UserId = "admin-001",
            Email = "admin@ordersystem.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            FullName = "Administrator",
            Role = UserRole.Admin,
            IsActive = true
        };

        var customerUser = new User
        {
            UserId = "customer-001",
            Email = "customer@ordersystem.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("customer123"),
            FullName = "Test Customer",
            Role = UserRole.Customer,
            IsActive = true
        };

        Users[adminUser.Email] = adminUser;
        Users[customerUser.Email] = customerUser;
    }

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication");

        group.MapPost("/login", Login)
            .WithName("Login")
            .WithDescription("Login with email and password");

        group.MapPost("/register", Register)
            .WithName("Register")
            .WithDescription("Register new user account");

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithDescription("Get current user info")
            .RequireAuthorization();

        group.MapPost("/refresh", RefreshToken)
            .WithName("RefreshToken")
            .WithDescription("Refresh JWT token");
    }

    private static IResult Login(
        LoginRequest request,
        AuthenticationService authService)
    {
        // Find user by email
        if (!Users.TryGetValue(request.Email, out var user))
        {
            return Results.Unauthorized();
        }

        // Verify password
        if (!authService.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Results.Unauthorized();
        }

        // Check if user is active
        if (!user.IsActive)
        {
            return Results.BadRequest(new MessageResponse("User account is inactive"));
        }

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;

        // Generate token
        var token = authService.GenerateToken(user);

        return Results.Ok(token);
    }

    private static IResult Register(
        RegisterRequest request,
        AuthenticationService authService)
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new MessageResponse("Email and password are required"));
        }

        // Check if user already exists
        if (Users.ContainsKey(request.Email))
        {
            return Results.BadRequest(new MessageResponse("User already exists"));
        }

        // Create new user
        var user = new User
        {
            UserId = Guid.NewGuid().ToString(),
            Email = request.Email,
            PasswordHash = authService.HashPassword(request.Password),
            FullName = request.FullName ?? request.Email.Split('@')[0],
            Role = UserRole.Customer,
            IsActive = true
        };

        Users[user.Email] = user;

        // Generate token
        var token = authService.GenerateToken(user);

        return Results.Created($"/api/auth/users/{user.UserId}", new UserRegisteredResponse(user.UserId, user.Email, user.FullName, token.AccessToken, token.TokenType, token.ExpiresIn));
    }

    private static IResult GetCurrentUser(HttpContext context)
    {
        var userIdClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        var emailClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email);
        var nameClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.Name);
        var roleClaim = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role);

        if (userIdClaim == null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new CurrentUserResponse(userIdClaim.Value, emailClaim?.Value, nameClaim?.Value, roleClaim?.Value));
    }

    private static IResult RefreshToken(
        RefreshTokenRequest request,
        AuthenticationService authService)
    {
        var principal = authService.ValidateToken(request.AccessToken);
        if (principal == null)
        {
            return Results.Unauthorized();
        }

        var emailClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.Email);
        if (emailClaim == null || !Users.TryGetValue(emailClaim.Value, out var user))
        {
            return Results.Unauthorized();
        }

        var newToken = authService.GenerateToken(user);
        return Results.Ok(newToken);
    }
}

/// <summary>
/// Login request DTO
/// </summary>
public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Register request DTO
/// </summary>
public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? FullName { get; set; }
}

/// <summary>
/// Refresh token request DTO
/// </summary>
public class RefreshTokenRequest
{
    public string AccessToken { get; set; } = string.Empty;
}
