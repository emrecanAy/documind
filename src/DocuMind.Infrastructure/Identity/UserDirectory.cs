using DocuMind.Application.Abstractions;
using DocuMind.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DocuMind.Infrastructure.Identity;

internal sealed class UserDirectory(AppDbContext db) : IUserDirectory
{
    public async Task<UserInfo?> FindByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = email.Trim().ToUpperInvariant();
        return await db.Users
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => new UserInfo(u.Id, u.Email!, u.DisplayName))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, UserInfo>> GetUsersAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users
            .Where(u => list.Contains(u.Id))
            .Select(u => new UserInfo(u.Id, u.Email!, u.DisplayName))
            .ToDictionaryAsync(u => u.Id, ct);
    }
}
