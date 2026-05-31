using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Infrastructure.Services.Seeds;

/// <summary>قالب آماده «تأییدیه کارفرما / اطلاعات بیمه‌ای» مطابق فرم استاندارد تأمین اجتماعی.</summary>
public static class EmployerConfirmationFieldGroupSeed
{
    /// <summary>شناسه ثابت برای seed و ارجاع در مستندات.</summary>
    public static readonly Guid TemplateId = Guid.Parse("7c4e9a20-8f1b-4a3d-9c2e-1a0b5c6d7e8f");

    private const int Heading = 10;
    private const int Paragraph = 11;
    private const int ShortText = 1;
    private const int LongText = 2;
    private const int PersianDate = 15;
    private const int CheckboxGroup = 14;

    public static async Task EnsureAsync(AppDbContext db, CancellationToken ct = default)
    {
        var row = await db.FormFieldGroupTemplates.FirstOrDefaultAsync(x => x.Id == TemplateId, ct);
        var fieldsJson = JsonSerializer.Serialize(BuildFields());
        var fieldCount = FormFieldGroupJsonHelper.CountNonHeaderFields(fieldsJson);
        var now = DateTime.UtcNow;

        if (row is not null)
        {
            row.Name = "تأییدیه کارفرما (تأمین اجتماعی)";
            row.Description = "فرم تأیید کارفرما، سوابق بیمه‌ای، تأیید مخاطب و بخش شعبه — قابل درج در فرم‌ساز";
            row.FieldsJson = fieldsJson;
            row.FieldCount = fieldCount;
            row.IsDeleted = false;
            row.DeletedAtUtc = null;
            row.UpdatedAtUtc = now;
        }
        else
        {
            db.FormFieldGroupTemplates.Add(new FormFieldGroupTemplate
            {
                Id = TemplateId,
                Name = "تأییدیه کارفرما (تأمین اجتماعی)",
                Description = "فرم تأیید کارفرما، سوابق بیمه‌ای، تأیید مخاطب و بخش شعبه — قابل درج در فرم‌ساز",
                FieldsJson = fieldsJson,
                FieldCount = fieldCount,
                IsDeleted = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private static List<Dictionary<string, object?>> BuildFields()
    {
        var list = new List<Dictionary<string, object?>>();
        var order = 0;

        void Add(
            Guid id,
            int fieldType,
            string label,
            string rowId,
            int colIndex,
            int rowColCount,
            bool required = false,
            string? placeholder = null,
            string? helpText = null,
            IReadOnlyList<string>? options = null)
        {
            list.Add(new Dictionary<string, object?>
            {
                ["id"] = id.ToString(),
                ["fieldType"] = fieldType,
                ["label"] = label,
                ["placeholder"] = placeholder ?? "",
                ["helpText"] = helpText ?? "",
                ["isRequired"] = required,
                ["sortOrder"] = order++,
                ["colSpan"] = 12,
                ["options"] = options is { Count: > 0 } ? options : null,
                ["rowId"] = rowId,
                ["colIndex"] = colIndex,
                ["rowColCount"] = rowColCount,
            });
        }

        // ── ۱. تأییدیه کارفرما ─────────────────────────────────────────────
        Add(Guid.Parse("a1000001-0000-4000-8000-000000000001"), Heading, "تأییدیه کارفرما", "r-emp", 0, 1);
        Add(Guid.Parse("a1000002-0000-4000-8000-000000000002"), Paragraph,
            "این قسمت توسط کارفرما تکمیل می‌شود.",
            "r-emp", 0, 1);

        Add(Guid.Parse("a1000003-0000-4000-8000-000000000003"), PersianDate, "تاریخ استخدام / بیمه", "r-emp-d1", 0, 2, true, "۱۴۰۴/۰۱/۰۱");
        Add(Guid.Parse("a1000004-0000-4000-8000-000000000004"), ShortText, "شغل", "r-emp-d1", 1, 2, true);

        Add(Guid.Parse("a1000005-0000-4000-8000-000000000005"), CheckboxGroup, "معاینات قبل از استخدام",
            "r-emp-d2", 0, 1, false, null, null, new[] { "دارد", "ندارد" });

        Add(Guid.Parse("a1000006-0000-4000-8000-000000000006"), ShortText, "نام کارفرما", "r-emp-d3", 0, 2, true);
        Add(Guid.Parse("a1000007-0000-4000-8000-000000000007"), ShortText, "شماره ملی کارفرما", "r-emp-d3", 1, 2, true, "۱۰ رقم");

        Add(Guid.Parse("a1000008-0000-4000-8000-000000000008"), ShortText, "شناسه حقوقی کارگاه", "r-emp-d4", 0, 2);
        Add(Guid.Parse("a1000009-0000-4000-8000-000000000009"), ShortText, "نام کارگاه", "r-emp-d4", 1, 2, true);

        Add(Guid.Parse("a1000010-0000-4000-8000-000000000010"), ShortText, "شماره کارگاه", "r-emp-d5", 0, 2);
        Add(Guid.Parse("a1000011-0000-4000-8000-000000000011"), ShortText, "تلفن کارگاه", "r-emp-d5", 1, 2, false, "09xxxxxxxxx");

        Add(Guid.Parse("a1000012-0000-4000-8000-000000000012"), LongText, "نشانی کارگاه", "r-emp-d6", 0, 1);

        Add(Guid.Parse("a1000013-0000-4000-8000-000000000013"), Paragraph, "محل امضاء کارفرما", "r-emp-d7", 0, 2);
        Add(Guid.Parse("a1000014-0000-4000-8000-000000000014"), Paragraph, "محل درج مهر کارگاه", "r-emp-d7", 1, 2);

        // ── ۲. پروانه اشتغال خارجی ─────────────────────────────────────────
        Add(Guid.Parse("a1000020-0000-4000-8000-000000000020"), Heading,
            "اطلاعات پروانه اشتغال مخاطب اصلی خارجی", "r-permit", 0, 1);
        Add(Guid.Parse("a1000021-0000-4000-8000-000000000021"), ShortText, "شماره مجوز", "r-permit-d1", 0, 2);
        Add(Guid.Parse("a1000022-0000-4000-8000-000000000022"), PersianDate, "تاریخ مجوز", "r-permit-d1", 1, 2);
        Add(Guid.Parse("a1000023-0000-4000-8000-000000000023"), PersianDate, "شروع دوره", "r-permit-d2", 0, 2);
        Add(Guid.Parse("a1000024-0000-4000-8000-000000000024"), PersianDate, "خاتمه دوره", "r-permit-d2", 1, 2);

        // ── ۳. بیماری خاص ───────────────────────────────────────────────────
        Add(Guid.Parse("a1000030-0000-4000-8000-000000000030"), Heading, "اطلاعات بیماری خاص", "r-disease", 0, 1);
        Add(Guid.Parse("a1000031-0000-4000-8000-000000000031"), CheckboxGroup, "نوع بیماری خاص",
            "r-disease-d1", 0, 1, false, null, null, new[] { "تالاسمی", "هموفیلی", "کلیوی" });
        Add(Guid.Parse("a1000032-0000-4000-8000-000000000032"), PersianDate, "تاریخ شروع بیماری خاص", "r-disease-d1", 0, 1);

        // ── ۴. سوابق بیمه‌ای (۵ ردیف) ───────────────────────────────────────
        Add(Guid.Parse("a1000040-0000-4000-8000-000000000040"), Heading,
            "اظهارات مخاطب در خصوص اطلاعات بیمه‌ای گذشته خود", "r-hist", 0, 1);
        Add(Guid.Parse("a1000041-0000-4000-8000-000000000041"), Paragraph,
            "سوابق بیمه را در ردیف‌های زیر وارد کنید (نوع مخاطب: اصلی / تبعی).",
            "r-hist", 0, 1);

        AddHistoryRow(1);
        AddHistoryRow(2);
        AddHistoryRow(3);
        AddHistoryRow(4);
        AddHistoryRow(5);

        void AddHistoryRow(int rowNum)
        {
            var p = rowNum.ToString("00");
            var row = $"r-hist-{rowNum}";
            Add(Guid.Parse($"b2{p}0001-0000-4000-8000-000000000001"), Heading, $"ردیف {rowNum}", row, 0, 1);
            Add(Guid.Parse($"b2{p}0002-0000-4000-8000-000000000002"), ShortText, "نوع مخاطب (اصلی / تبعی)", row + "-a", 0, 2);
            Add(Guid.Parse($"b2{p}0003-0000-4000-8000-000000000003"), ShortText, "شماره بیمه مخاطب اصلی", row + "-a", 1, 2);
            Add(Guid.Parse($"b2{p}0004-0000-4000-8000-000000000004"), ShortText, "نام کارگاه", row + "-b", 0, 2);
            Add(Guid.Parse($"b2{p}0005-0000-4000-8000-000000000005"), ShortText, "کد کارگاه", row + "-b", 1, 2);
            Add(Guid.Parse($"b2{p}0006-0000-4000-8000-000000000006"), PersianDate, "دوره ارتباط — از تاریخ", row + "-c", 0, 2);
            Add(Guid.Parse($"b2{p}0007-0000-4000-8000-000000000007"), PersianDate, "دوره ارتباط — تا تاریخ", row + "-c", 1, 2);
            Add(Guid.Parse($"b2{p}0008-0000-4000-8000-000000000008"), ShortText, "شغل", row + "-d", 0, 2);
            Add(Guid.Parse($"b2{p}0009-0000-4000-8000-000000000009"), ShortText, "شعبه", row + "-d", 1, 2);
            Add(Guid.Parse($"b2{p}000a-0000-4000-8000-00000000000a"), ShortText, "استان", row + "-e", 0, 1);
        }

        // ── ۵. تأییدیه مخاطب ────────────────────────────────────────────────
        Add(Guid.Parse("a1000050-0000-4000-8000-000000000050"), Heading, "تأییدیه مخاطب", "r-subj", 0, 1);
        Add(Guid.Parse("a1000051-0000-4000-8000-000000000051"), ShortText, "نام و نام خانوادگی (اینجانب)", "r-subj-d1", 0, 2, true);
        Add(Guid.Parse("a1000052-0000-4000-8000-000000000052"), PersianDate, "تاریخ مراجعه", "r-subj-d1", 1, 2, true);
        Add(Guid.Parse("a1000053-0000-4000-8000-000000000053"), ShortText, "شعبه", "r-subj-d2", 0, 1, true);
        Add(Guid.Parse("a1000054-0000-4000-8000-000000000054"), Paragraph,
            "اظهار می‌نمایم اطلاعات فوق صحیح است.",
            "r-subj-d3", 0, 1);
        Add(Guid.Parse("a1000055-0000-4000-8000-000000000055"), Paragraph, "محل امضاء", "r-subj-d4", 0, 2);
        Add(Guid.Parse("a1000056-0000-4000-8000-000000000056"), Paragraph, "محل درج اثر انگشت", "r-subj-d4", 1, 2);

        // ── ۶. بخش شعبه ─────────────────────────────────────────────────────
        Add(Guid.Parse("a1000060-0000-4000-8000-000000000060"), Heading,
            "اطلاعات بیمه‌ای مخاطب و تأییدیه مسئول نامنویسی", "r-branch", 0, 1);
        Add(Guid.Parse("a1000061-0000-4000-8000-000000000061"), Paragraph,
            "این قسمت توسط شعبه تکمیل می‌شود.",
            "r-branch", 0, 1);

        Add(Guid.Parse("a1000062-0000-4000-8000-000000000062"), CheckboxGroup, "نحوه شناسایی",
            "r-branch-d1", 0, 1, false, null, null, new[] { "کارفرما", "بازرسی", "عقد قرارداد" });
        Add(Guid.Parse("a1000063-0000-4000-8000-000000000063"), CheckboxGroup, "نوع ارتباط",
            "r-branch-d2", 0, 1, false, null, null, new[] { "بیمه پرداز", "دریافت کننده", "تحت پوشش شده اصلی", "بازنشسته" });

        Add(Guid.Parse("a1000064-0000-4000-8000-000000000064"), ShortText, "نوع بیمه", "r-branch-d3", 0, 2);
        Add(Guid.Parse("a1000065-0000-4000-8000-000000000065"), ShortText, "نوع خدمت", "r-branch-d3", 1, 2);
        Add(Guid.Parse("a1000066-0000-4000-8000-000000000066"), ShortText, "گرایش بیمه / خدمت", "r-branch-d4", 0, 1);

        Add(Guid.Parse("a1000067-0000-4000-8000-000000000067"), ShortText, "نام مسئول نامنویسی", "r-branch-d5", 0, 2);
        Add(Guid.Parse("a1000068-0000-4000-8000-000000000068"), PersianDate, "تاریخ تأیید شعبه", "r-branch-d5", 1, 2);

        Add(Guid.Parse("a1000069-0000-4000-8000-000000000069"), ShortText, "شماره بیمه تأمین اجتماعی (۱۰ رقم)",
            "r-branch-d6", 0, 1, false, "۱۲۳۴۵۶۷۸۹۰");

        Add(Guid.Parse("a1000070-0000-4000-8000-000000000070"), Paragraph,
            "مهر و امضاء مسئول نامنویسی و حساب‌های انفرادی",
            "r-branch-d7", 0, 1);

        return list;
    }
}
