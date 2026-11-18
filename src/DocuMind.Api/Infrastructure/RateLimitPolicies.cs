using System.Threading.RateLimiting;

namespace DocuMind.Api.Infrastructure;

internal static class RateLimitPolicies
{
    public const string Chat = "chat";
    public const string Auth = "auth";

    public static IServiceCollection AddDocuMindRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // LLM calls are the expensive resource: limit per user.
            options.AddPolicy(Chat, context => RateLimitPartition.GetFixedWindowLimiter(
                HttpCurrentUser.GetUserId(context.User)?.ToString() ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));

            // Brute-force protection for credential endpoints: limit per IP.
            options.AddPolicy(Auth, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
        });
        return services;
    }
}
