using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class ExamLinkCodeGenerator
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static async Task<string> GenerateUniqueAsync(AppDbContext db, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => Chars[Random.Shared.Next(Chars.Length)])
                .ToArray());
            if (!await db.ExamLinks.AnyAsync(x => x.Code == code, ct))
                return code;
        }

        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }
}
