using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DocuMind.Application.Common;
using DocuMind.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DocuMind.Infrastructure.Identity;

public sealed class UnauthorizedException(string message) : Exception(message);

/// <summary>
/// JWT access tokens (short lived) + opaque refresh tokens (rotated on every use, hashed at rest).
/// Re-use of a revoked refresh token revokes the whole token family for that user (theft detection).
/// </summary>
public sealed class AuthService(
    UserManager<ApplicationUser> users,
    AppDbContext db,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider clock)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = request.Email.Trim(),
            UserName = request.Email.Trim(),
            DisplayName = request.DisplayName.Trim(),
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "DuplicateUserName" or "DuplicateEmail"))
                throw new ConflictException("An account with this email already exists.");
            throw new AppValidationException(result.Errors
                .GroupBy(e => e.Code.Contains("Password", StringComparison.Ordinal) ? "password" : "email")
                .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
        }

        return await IssueAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            throw new UnauthorizedException("Invalid email or password.");

        if (await users.IsLockedOutAsync(user))
            throw new UnauthorizedException("Account is temporarily locked. Try again later.");

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            throw new UnauthorizedException("Invalid email or password.");
        }

        await users.ResetAccessFailedCountAsync(user);
        return await IssueAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
                    ?? throw new UnauthorizedException("Invalid refresh token.");

        if (token.RevokedAt is not null)
        {
            // Reuse of a rotated token: assume theft, revoke every active token of the user.
            await db.RefreshTokens
                .Where(t => t.UserId == token.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
            throw new UnauthorizedException("Refresh token was already used.");
        }

        if (token.ExpiresAt <= clock.GetUtcNow())
            throw new UnauthorizedException("Refresh token expired.");

        var user = await users.FindByIdAsync(token.UserId.ToString())
                   ?? throw new UnauthorizedException("User no longer exists.");

        var response = await IssueAsync(user, ct, rotated: token);
        return response;
    }

    public async Task LogoutAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.GetUtcNow()), ct);
    }

    public async Task<UserDto> GetUserAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException("User not found.");
        return ToDto(user);
    }

    private async Task<AuthResponse> IssueAsync(ApplicationUser user, CancellationToken ct, RefreshToken? rotated = null)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        };
        var accessToken = new JsonWebTokenHandler().CreateToken(descriptor);

        var refreshValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var refresh = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshValue),
            ExpiresAt = now.AddDays(_jwt.RefreshTokenDays),
        };
        db.RefreshTokens.Add(refresh);

        if (rotated is not null)
        {
            rotated.RevokedAt = now;
            rotated.ReplacedById = refresh.Id;
        }

        await db.SaveChangesAsync(ct);
        return new AuthResponse(accessToken, expires, refreshValue, ToDto(user));
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static UserDto ToDto(ApplicationUser user) => new(user.Id, user.Email ?? string.Empty, user.DisplayName);
}
