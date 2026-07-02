using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class ResponderLookupHelper
{
    public static bool IsValidNationalCode(string? code) =>
        !string.IsNullOrWhiteSpace(code);

    public static string NormalizeMobile(string? mobile) =>
        FormSubmissionMobileHelper.NormalizeMobile(mobile);

    public static bool IsValidMobile(string? mobile) =>
        FormSubmissionMobileHelper.IsValidMobile(mobile);

    public static string NormalizeNationalCode(string code) => code.Trim();

    public static IQueryable<Responder> ActiveOnly(AppDbContext db) =>
        db.Responders.Where(x => !x.IsDeleted);

    public static async Task<Responder?> FindByNationalCodeAsync(
        AppDbContext db,
        string nationalCode,
        CancellationToken ct = default)
    {
        var code = NormalizeNationalCode(nationalCode);
        return await ActiveOnly(db).FirstOrDefaultAsync(x => x.NationalCode == code, ct);
    }

    public static async Task<Responder> FindOrCreateForDispatchAsync(
        AppDbContext db,
        string nationalCode,
        string fullName,
        string mobile,
        UserGender? gender,
        Guid? createdByUserId,
        CancellationToken ct = default)
    {
        var code = NormalizeNationalCode(nationalCode);
        var name = fullName.Trim();
        var mob = NormalizeMobile(mobile);

        var existing = await ActiveOnly(db).FirstOrDefaultAsync(x => x.NationalCode == code, ct);
        if (existing is not null)
            return await UpdateExistingForDispatchAsync(db, existing, name, mob, gender, ct);

        if (IsValidMobile(mob))
        {
            var byMobile = await ActiveOnly(db).FirstOrDefaultAsync(x => x.MobileNumber == mob, ct);
            if (byMobile is not null)
            {
                var codeTaken = await ActiveOnly(db).AnyAsync(x => x.NationalCode == code && x.Id != byMobile.Id, ct);
                if (codeTaken)
                    throw new InvalidOperationException("این کد ملی قبلاً برای پاسخگوی دیگری ثبت شده است");

                byMobile.NationalCode = code;
                return await UpdateExistingForDispatchAsync(db, byMobile, name, mob, gender, ct);
            }
        }

        await EnsureNationalCodeUniqueAsync(db, null, code, ct);
        await EnsureMobileUniqueAsync(db, null, mob, ct);

        var entity = new Responder
        {
            Id = Guid.NewGuid(),
            NationalCode = code,
            FullName = name,
            MobileNumber = mob,
            Gender = gender,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.Responders.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    private static async Task<Responder> UpdateExistingForDispatchAsync(
        AppDbContext db,
        Responder existing,
        string name,
        string mobile,
        UserGender? gender,
        CancellationToken ct)
    {
        if (existing.IsDeleted)
            throw new InvalidOperationException("پاسخگو یافت نشد");

        if (name.Length >= 2)
            existing.FullName = name;
        if (gender is not null)
            existing.Gender = gender;

        if (IsValidMobile(mobile))
        {
            var mobileTaken = await db.Responders.IgnoreQueryFilters().AnyAsync(
                x => !x.IsDeleted && x.MobileNumber == mobile && x.Id != existing.Id,
                ct);
            if (!mobileTaken)
                existing.MobileNumber = mobile;
        }

        await db.SaveChangesAsync(ct);
        return existing;
    }

    public static async Task EnsureNationalCodeUniqueAsync(
        AppDbContext db,
        Guid? excludeId,
        string nationalCode,
        CancellationToken ct = default)
    {
        var code = NormalizeNationalCode(nationalCode);
        var q = ActiveOnly(db).Where(x => x.NationalCode == code);
        if (excludeId is Guid id)
            q = q.Where(x => x.Id != id);
        if (await q.AnyAsync(ct))
            throw new InvalidOperationException("این کد ملی قبلاً ثبت شده است");
    }

    public static async Task EnsureMobileUniqueAsync(
        AppDbContext db,
        Guid? excludeId,
        string mobile,
        CancellationToken ct)
    {
        if (!IsValidMobile(mobile))
            return;
        var mob = NormalizeMobile(mobile);
        var q = ActiveOnly(db).Where(x => x.MobileNumber == mob);
        if (excludeId is Guid id)
            q = q.Where(x => x.Id != id);
        if (await q.AnyAsync(ct))
            throw new InvalidOperationException("این شماره موبایل قبلاً ثبت شده است");
    }
}
