using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>محدودسازی مشاهده و ارجاع فرم بر اساس نقش، پرمیژن و FormUserAccesses.</summary>
public static class FormVisibilityQuery
{
    public static bool CanReadAllForms(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("permission", "forms.read.all");

    public static bool CanManageAllFormAccess(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("permission", "forms.access.read.all");

    public static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    public static bool UserOwnsForm(Form form, Guid userId)
    {
        if (string.IsNullOrWhiteSpace(form.UserId)) return false;
        if (Guid.TryParse(form.UserId, out var ownerId))
            return ownerId == userId;
        var d = userId.ToString("D");
        var n = userId.ToString("N");
        return string.Equals(form.UserId, d, StringComparison.OrdinalIgnoreCase)
            || string.Equals(form.UserId, n, StringComparison.OrdinalIgnoreCase)
            || string.Equals(form.UserId, userId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>فرم‌های قابل مشاهده در لیست فرم، ارسال، و غیره.</summary>
    public static IQueryable<Form> ApplyVisibleForms(this IQueryable<Form> query, AppDbContext db, ClaimsPrincipal user)
    {
        if (CanReadAllForms(user))
            return query;

        var userId = GetUserId(user);
        if (userId is null)
            return query.Where(_ => false);

        var uid = userId.Value;
        return query.Where(f =>
            db.FormUserAccesses.Any(a => a.FormId == f.Id && a.UserId == uid)
            || (f.UserId != null && (
                f.UserId == uid.ToString()
                || f.UserId == uid.ToString("D")
                || f.UserId == uid.ToString("N"))));
    }

    public static bool CanReadAllFormSubmissions(ClaimsPrincipal user) =>
        CanReadAllForms(user)
        || user.HasClaim("permission", "workflow-runs.read.all");

    /// <summary>
    /// پاسخ‌های فرم / گردش کار: فرم‌های ارجاع‌شده یا ساخته‌شده، لینک ارسال‌شده، یا نقش در گردش.
    /// </summary>
    public static IQueryable<FormSubmission> ApplyVisibleFormSubmissions(
        this IQueryable<FormSubmission> query,
        AppDbContext db,
        ClaimsPrincipal user)
    {
        if (CanReadAllFormSubmissions(user))
            return query;

        var userId = GetUserId(user);
        if (userId is null)
            return query.Where(_ => false);

        var uid = userId.Value;
        var idStr = uid.ToString();
        var uidD = uid.ToString("D");
        var uidN = uid.ToString("N");

        return query.Where(s =>
            s.Form != null
            && !s.Form.IsDeleted
            && (
                db.FormUserAccesses.Any(a => a.FormId == s.FormId && a.UserId == uid)
                || (s.Form.UserId != null && (s.Form.UserId == idStr || s.Form.UserId == uidD || s.Form.UserId == uidN))
                || (s.DispatchLinkId != null
                    && db.FormDispatchLinks.Any(l => l.Id == s.DispatchLinkId && l.SentByUserId == uid))
                || (s.StepsJson != null && s.StepsJson.Contains(idStr))));
    }

    /// <summary>فرم‌هایی که کاربر می‌تواند در صفحه ارجاع فرم، دسترسی دیگران را تنظیم کند.</summary>
    public static IQueryable<Form> ApplyFormsForAccessDelegation(this IQueryable<Form> query, ClaimsPrincipal user)
    {
        if (CanManageAllFormAccess(user))
            return query;

        var userId = GetUserId(user);
        if (userId is null)
            return query.Where(_ => false);

        var uid = userId.Value;
        return query.Where(f => f.UserId != null && (
            f.UserId == uid.ToString()
            || f.UserId == uid.ToString("D")
            || f.UserId == uid.ToString("N")));
    }
}
