using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Sms;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Infrastructure.Services.ExamDispatch;

public record ExamDispatchPreviewDto(
    Guid ExamFormId,
    string ExamTitle,
    int TotalParticipants,
    int InvalidMobileCount,
    int ScorableQuestionCount);

public record ExamDispatchSendResultDto(
    Guid DispatchId,
    int TotalParticipants,
    int SentCount,
    int FailedCount);

public class ExamDispatchGroupSendService(
    AppDbContext db,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IFrontendUrlResolver frontendUrls)
{
    public async Task<ExamDispatchPreviewDto?> PreviewAsync(
        Guid examFormId,
        IReadOnlyList<Guid> groupIds,
        CancellationToken ct = default)
    {
        if (examFormId == Guid.Empty || groupIds.Count == 0) return null;

        var form = await db.ExamForms.AsNoTracking()
            .Include(x => x.Questions)
            .FirstOrDefaultAsync(x => x.Id == examFormId && !x.IsDeleted && x.IsActive, ct);
        if (form is null) return null;

        var participants = await LoadParticipantsAsync(groupIds, ct);
        var invalidMobile = participants.Count(p => !UserFieldNormalizer.IsValidMobile(p.MobileNumber));
        var scorable = ExamScoringHelper.CountScorableQuestions(form.Questions);
        return new ExamDispatchPreviewDto(form.Id, form.Title, participants.Count, invalidMobile, scorable);
    }

    public async Task<ExamDispatchSendResultDto> SendAsync(
        Guid examFormId,
        IReadOnlyList<Guid> groupIds,
        DateTime windowStartAtUtc,
        DateTime windowEndAtUtc,
        int passingCorrectCount,
        Guid? createdByUserId,
        string? examTitle = null,
        CancellationToken ct = default)
    {
        if (examFormId == Guid.Empty)
            throw new InvalidOperationException("فرم آزمون را انتخاب کنید");
        if (groupIds.Count == 0)
            throw new InvalidOperationException("حداقل یک گروه انتخاب کنید");
        if (windowStartAtUtc >= windowEndAtUtc)
            throw new InvalidOperationException("زمان پایان باید بعد از زمان شروع باشد");
        if (passingCorrectCount < 1)
            throw new InvalidOperationException("حداقل پاسخ صحیح برای قبولی باید حداقل ۱ باشد");

        var form = await db.ExamForms
            .Include(x => x.Questions)
            .FirstOrDefaultAsync(x => x.Id == examFormId && !x.IsDeleted && x.IsActive, ct)
            ?? throw new InvalidOperationException("فرم آزمون یافت نشد");

        if (!string.IsNullOrWhiteSpace(examTitle))
        {
            form.Title = examTitle.Trim();
            form.UpdatedAtUtc = DateTime.UtcNow;
        }

        var scorable = ExamScoringHelper.CountScorableQuestions(form.Questions);
        if (scorable < 1)
            throw new InvalidOperationException("فرم آزمون باید حداقل یک سوال گزینه‌ای با پاسخ صحیح داشته باشد");
        if (passingCorrectCount > scorable)
            throw new InvalidOperationException($"حداقل پاسخ صحیح نمی‌تواند بیشتر از {scorable} باشد");

        var participants = await LoadParticipantsAsync(groupIds, ct);
        if (participants.Count == 0)
            throw new InvalidOperationException("هیچ آزمون‌دهنده‌ای در گروه‌های انتخاب‌شده یافت نشد");

        await smsPatterns.EnsureSeededAsync(ct);
        var baseUrl = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("آدرس پایهٔ عمومی در تنظیمات سایت تعریف نشده است");

        var dispatch = new Domain.Entities.ExamDispatch
        {
            Id = Guid.NewGuid(),
            ExamFormId = form.Id,
            WindowStartAtUtc = windowStartAtUtc,
            WindowEndAtUtc = windowEndAtUtc,
            GroupIdsJson = JsonSerializer.Serialize(groupIds),
            PassingCorrectCount = passingCorrectCount,
            TotalParticipants = participants.Count,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.ExamDispatches.Add(dispatch);

        var sent = 0;
        var failed = 0;
        foreach (var p in participants)
        {
            var ok = await DispatchOneAsync(
                form, dispatch, p, baseUrl, createdByUserId, ct);
            if (ok) sent++; else failed++;
        }

        dispatch.SentCount = sent;
        dispatch.FailedCount = failed;
        await db.SaveChangesAsync(ct);

        return new ExamDispatchSendResultDto(dispatch.Id, participants.Count, sent, failed);
    }

    private async Task<bool> DispatchOneAsync(
        ExamForm form,
        Domain.Entities.ExamDispatch dispatch,
        ParticipantRow participant,
        string baseUrl,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        var mobile = UserFieldNormalizer.NormalizeMobile(participant.MobileNumber);
        if (!UserFieldNormalizer.IsValidMobile(mobile))
            return false;

        var code = await ExamLinkCodeGenerator.GenerateUniqueAsync(db, ct);
        var fullName = $"{participant.FirstName} {participant.LastName}".Trim();
        db.ExamLinks.Add(new ExamLink
        {
            Id = Guid.NewGuid(),
            ExamFormId = form.Id,
            ExamDispatchId = dispatch.Id,
            ExamParticipantId = participant.Id,
            Code = code,
            ParticipantName = fullName,
            ParticipantMobile = mobile,
            WindowStartAtUtc = dispatch.WindowStartAtUtc,
            WindowEndAtUtc = dispatch.WindowEndAtUtc,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        var link = $"{baseUrl.TrimEnd('/')}/exams/fill?c={code}";
        var (startDate, startTime) = SmsDateTimeFormatter.FormatUtcTehran(dispatch.WindowStartAtUtc);
        var (endDate, endTime) = SmsDateTimeFormatter.FormatUtcTehran(dispatch.WindowEndAtUtc);
        var msg = await smsPatterns.RenderAsync("exam.dispatch.link.default", SmsPatternVars.Dict(
            ("firstName", participant.FirstName),
            ("lastName", participant.LastName),
            ("fullName", fullName),
            ("examTitle", form.Title),
            ("link", link),
            ("startDate", startDate),
            ("startTime", startTime),
            ("endDate", endDate),
            ("endTime", endTime),
            ("startAt", $"{startDate} {startTime}"),
            ("endAt", $"{endDate} {endTime}")
        ), ct);

        if (string.IsNullOrWhiteSpace(msg))
            return false;

        var req = new SmsRequest(mobile, msg, "exam.dispatch", SkipThrottle: true);
        var ok = await smsSender.SendSmsAsync(req, ct);
        if (ok) return true;
        return await smsSender.SendSmsAsync(req, ct);
    }

    private async Task<List<ParticipantRow>> LoadParticipantsAsync(
        IReadOnlyList<Guid> groupIds,
        CancellationToken ct)
    {
        var ids = groupIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return [];

        var rows = await (
            from m in db.ExamParticipantGroupMembers.AsNoTracking()
            where ids.Contains(m.GroupId)
            join p in db.ExamParticipants.AsNoTracking() on m.ParticipantId equals p.Id
            where !p.IsDeleted && p.IsActive
            select new ParticipantRow(p.Id, p.FirstName, p.LastName, p.MobileNumber)
        ).ToListAsync(ct);

        return rows
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .OrderBy(x => x.LastName)
            .ThenBy(x => x.FirstName)
            .ToList();
    }

    private sealed record ParticipantRow(Guid Id, string FirstName, string LastName, string MobileNumber);
}
