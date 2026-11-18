using DocuMind.Api.Infrastructure;
using DocuMind.Infrastructure.Identity;

namespace DocuMind.Api.Endpoints;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, AuthService auth, CancellationToken ct) =>
                TypedResults.Ok(await auth.RegisterAsync(request, ct)))
            .Validate<RegisterRequest>()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AllowAnonymous();

        group.MapPost("/login", async (LoginRequest request, AuthService auth, CancellationToken ct) =>
                TypedResults.Ok(await auth.LoginAsync(request, ct)))
            .Validate<LoginRequest>()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AllowAnonymous();

        group.MapPost("/refresh", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
                TypedResults.Ok(await auth.RefreshAsync(request, ct)))
            .Validate<RefreshRequest>()
            .AllowAnonymous();

        group.MapPost("/logout", async (RefreshRequest request, AuthService auth, CancellationToken ct) =>
            {
                await auth.LogoutAsync(request, ct);
                return TypedResults.NoContent();
            })
            .Validate<RefreshRequest>()
            .AllowAnonymous();

        group.MapGet("/me", async (HttpContext http, AuthService auth) =>
                TypedResults.Ok(await auth.GetUserAsync(HttpCurrentUser.GetUserId(http.User)!.Value)))
            .RequireAuthorization();

        return app;
    }
}
