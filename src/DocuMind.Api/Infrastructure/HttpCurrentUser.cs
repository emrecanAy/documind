using System.Security.Claims;
using DocuMind.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DocuMind.Api.Infrastructure;

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId => GetUserId(accessor.HttpContext?.User)
                          ?? throw new InvalidOperationException("No authenticated user in the current context.");

    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public static Guid? GetUserId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
