using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class InboxMessageService(AppDbContext db, UserManager<AppUser> userManager) : IInboxMessageService
{
    public async Task SendToUserAsync(Guid userId, string title, string body, CancellationToken cancellationToken = default)
    {
        var trimmedTitle = (title ?? "").Trim();
        var trimmedBody = (body ?? "").Trim();
        if (string.IsNullOrWhiteSpace(trimmedTitle) || string.IsNullOrWhiteSpace(trimmedBody))
            return;

        db.InboxMessages.Add(new InboxMessage
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = trimmedTitle.Length > 200 ? trimmedTitle[..200] : trimmedTitle,
            Body = trimmedBody.Length > 2000 ? trimmedBody[..2000] : trimmedBody,
            IsRead = false,
            IsArchived = false,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SendToMobileAsync(string mobileNumber, string title, string body, CancellationToken cancellationToken = default)
    {
        var phone = NormalizeMobile(mobileNumber);
        if (string.IsNullOrWhiteSpace(phone)) return;

        var user = await userManager.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);
        if (user is null) return;

        await SendToUserAsync(user.Id, title, body, cancellationToken);
    }

    private static string NormalizeMobile(string value)
    {
        var digits = (value ?? "")
            .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
            .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
            .Trim();
        return new string(digits.Where(char.IsDigit).ToArray());
    }
}
