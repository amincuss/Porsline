using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

/// <summary>محدودسازی مشاهده قرارداد: ایجادکننده، تأییدکننده در گردش، یا read.all.</summary>
public static class ContractVisibilityQuery
{
    public static bool CanReadAllContracts(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("permission", "contracts.read.all");

    public static bool CanReadAllContractsArchive(ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.HasClaim("permission", "contracts.archive.read.all");

    public static Guid? GetUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var g) ? g : null;

    public static IQueryable<Contract> ApplyVisibleContracts(this IQueryable<Contract> query, ClaimsPrincipal user)
    {
        if (CanReadAllContracts(user))
            return query;

        var userId = GetUserId(user);
        if (userId is null)
            return query.Where(_ => false);

        var idStr = userId.Value.ToString();
        return query.Where(c =>
            c.CreatedByUserId == userId.Value
            || (c.StepsJson != null && c.StepsJson.Contains(idStr)));
    }

    public static IQueryable<Contract> ApplyVisibleArchivedContracts(this IQueryable<Contract> query, ClaimsPrincipal user)
    {
        if (CanReadAllContractsArchive(user))
            return query;

        var userId = GetUserId(user);
        if (userId is null)
            return query.Where(_ => false);

        var idStr = userId.Value.ToString();
        return query.Where(c =>
            c.CreatedByUserId == userId.Value
            || (c.StepsJson != null && c.StepsJson.Contains(idStr)));
    }
}
