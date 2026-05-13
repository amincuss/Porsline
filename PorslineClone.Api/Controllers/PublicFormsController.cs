using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public/forms")]
[AllowAnonymous]
public class PublicFormsController(
    AppDbContext db,
    IWebHostEnvironment env,
    ISmsSender smsSender,
    IFrontendUrlResolver frontendUrls) : ControllerBase
{
    private static string NormalizeMobile(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var mapped = input.Trim()
            .Replace('۰', '0').Replace('۱', '1').Replace('۲', '2').Replace('۳', '3').Replace('۴', '4')
            .Replace('۵', '5').Replace('۶', '6').Replace('۷', '7').Replace('۸', '8').Replace('۹', '9')
            .Replace('٠', '0').Replace('١', '1').Replace('٢', '2').Replace('٣', '3').Replace('٤', '4')
            .Replace('٥', '5').Replace('٦', '6').Replace('٧', '7').Replace('٨', '8').Replace('٩', '9');
        return new string(mapped.Where(char.IsDigit).ToArray());
    }

    [HttpGet("access")]
    public async Task<IActionResult> Access([FromQuery] string c, CancellationToken ct)
    {
        var link = await db.FormDispatchLinks.FirstOrDefaultAsync(x => x.Code == c, ct);
        if (link is null || !link.IsActive || link.ExpiresAtUtc < DateTime.UtcNow || link.UsedAtUtc != null)
            return BadRequest(new { message = "لینک فرم نامعتبر یا منقضی است" });

        var form = await db.Forms
            .Include(f => f.Fields.OrderBy(x => x.SortOrder))
            .FirstOrDefaultAsync(f => f.Id == link.FormId && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        if (!form.IsActive) return BadRequest(new { message = "این فرم غیرفعال است" });
        if (form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "اعتبار این فرم به پایان رسیده است" });
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct);
        var requireOtp = smsSettings?.PublicFormRequireOtp ?? false;
        if (requireOtp && link.OtpVerifiedAtUtc is null)
        {
            return Ok(new
            {
                requiresOtp = true,
                title = form.Title,
                mobileNumber = NormalizeMobile(link.ResponderMobileNumber)
            });
        }

        return Ok(new
        {
            form.Id,
            form.Title,
            form.Description,
            form.ExpiresAtUtc,
            form.QuestionDisplayMode,
            RequiresOtp = false,
            Responder = new { link.ResponderId, FullName = link.ResponderFullName, MobileNumber = link.ResponderMobileNumber },
            Fields = form.Fields.Select(f => new
            {
                f.Id,
                f.FieldType,
                f.Label,
                f.Placeholder,
                f.HelpText,
                f.IsRequired,
                f.SortOrder,
                f.ColSpan,
                f.UploadMaxSizeMb,
                Options = f.OptionsJson != null ? JsonSerializer.Deserialize<List<string>>(f.OptionsJson) : null
            })
        });
    }

    [HttpPost("access/otp/send")]
    public async Task<IActionResult> SendAccessOtp([FromBody] PublicAccessOtpSendRequest req, CancellationToken ct)
    {
        var link = await db.FormDispatchLinks.FirstOrDefaultAsync(x => x.Code == req.Code, ct);
        if (link is null || !link.IsActive || link.ExpiresAtUtc < DateTime.UtcNow || link.UsedAtUtc != null)
            return BadRequest(new { message = "لینک فرم نامعتبر یا منقضی است" });

        var expectedMobile = NormalizeMobile(link.ResponderMobileNumber);
        var requestedMobile = NormalizeMobile(req.MobileNumber);
        if (string.IsNullOrWhiteSpace(expectedMobile) || expectedMobile.Length != 11)
            return BadRequest(new { message = "شماره موبایل پاسخگو برای این لینک معتبر نیست" });
        if (!string.IsNullOrWhiteSpace(requestedMobile) && !string.Equals(requestedMobile, expectedMobile, StringComparison.Ordinal))
            return BadRequest(new { message = "شماره موبایل با لینک ارسالی یکسان نیست" });

        var code = Random.Shared.Next(100000, 999999).ToString();
        db.ResponderOtpCodes.Add(new ResponderOtpCode
        {
            Id = Guid.NewGuid(),
            ResponderId = link.ResponderId,
            MobileNumber = expectedMobile,
            Code = code,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(2)
        });
        await db.SaveChangesAsync(ct);

        var isDev = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Development", StringComparison.OrdinalIgnoreCase);
        if (!isDev)
        {
            await smsSender.SendSmsAsync(new Application.Contracts.SmsRequest(expectedMobile, $"کد تایید فرم: {code}"), ct);
        }

        return Ok(new { message = "کد تایید ارسال شد", otpCode = isDev ? code : null });
    }

    [HttpPost("access/otp/verify")]
    public async Task<IActionResult> VerifyAccessOtp([FromBody] PublicAccessOtpVerifyRequest req, CancellationToken ct)
    {
        var link = await db.FormDispatchLinks.FirstOrDefaultAsync(x => x.Code == req.Code, ct);
        if (link is null || !link.IsActive || link.ExpiresAtUtc < DateTime.UtcNow || link.UsedAtUtc != null)
            return BadRequest(new { message = "لینک فرم نامعتبر یا منقضی است" });

        var expectedMobile = NormalizeMobile(link.ResponderMobileNumber);
        var requestedMobile = NormalizeMobile(req.MobileNumber);
        if (string.IsNullOrWhiteSpace(expectedMobile) || expectedMobile.Length != 11)
            return BadRequest(new { message = "شماره موبایل پاسخگو برای این لینک معتبر نیست" });
        if (!string.IsNullOrWhiteSpace(requestedMobile) && !string.Equals(requestedMobile, expectedMobile, StringComparison.Ordinal))
            return BadRequest(new { message = "شماره موبایل با لینک ارسالی یکسان نیست" });

        var otpQuery = db.ResponderOtpCodes
            .Where(x => x.MobileNumber == expectedMobile && !x.IsUsed && x.ExpiresAtUtc > DateTime.UtcNow);
        if (link.ResponderId != Guid.Empty)
            otpQuery = otpQuery.Where(x => x.ResponderId == link.ResponderId);

        var otp = await otpQuery.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (otp is null || otp.Code != req.Otp)
            return BadRequest(new { message = "کد تایید نامعتبر است" });

        otp.IsUsed = true;
        link.OtpVerifiedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "احراز هویت با موفقیت انجام شد" });
    }

    [HttpPost("submit")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Submit([FromForm] PublicSubmitRequest req, CancellationToken ct)
    {
        var link = await db.FormDispatchLinks.FirstOrDefaultAsync(x => x.Code == req.Code, ct);
        if (link is null || !link.IsActive || link.ExpiresAtUtc < DateTime.UtcNow || link.UsedAtUtc != null)
            return BadRequest(new { message = "لینک فرم نامعتبر یا منقضی است" });

        var form = await db.Forms
            .Include(f => f.Fields.OrderBy(x => x.SortOrder))
            .FirstOrDefaultAsync(f => f.Id == link.FormId && !f.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        if (!form.IsActive) return BadRequest(new { message = "این فرم غیرفعال است" });
        if (form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "اعتبار این فرم به پایان رسیده است" });
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct);
        var requireOtp = smsSettings?.PublicFormRequireOtp ?? false;
        if (requireOtp && link.OtpVerifiedAtUtc is null)
            return BadRequest(new { message = "ابتدا احراز هویت OTP انجام شود" });

        var responder = await db.Responders.FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct);
        if (responder is null)
        {
            responder = new Responder
            {
                Id = link.ResponderId != Guid.Empty ? link.ResponderId : Guid.NewGuid(),
                FullName = link.ResponderFullName,
                MobileNumber = link.ResponderMobileNumber,
                CreatedAtUtc = DateTime.UtcNow
            };
            db.Responders.Add(responder);
        }
        else
        {
            responder.FullName = link.ResponderFullName;
            responder.MobileNumber = link.ResponderMobileNumber;
        }

        var values = string.IsNullOrWhiteSpace(req.ValuesJson)
            ? new Dictionary<string, string>()
            : JsonSerializer.Deserialize<Dictionary<string, string>>(req.ValuesJson) ?? new Dictionary<string, string>();

        var responderFolder = link.ResponderId == Guid.Empty ? responder.Id.ToString() : link.ResponderId.ToString();
        var uploadRoot = Path.Combine(env.ContentRootPath, "Formupload", responderFolder);
        Directory.CreateDirectory(uploadRoot);
        var filesByFieldId = Request.Form.Files.Where(f => f.Name.StartsWith("file_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Name["file_".Length..], f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var ff in form.Fields.Where(x => (int)x.FieldType == 9 || (int)x.FieldType == 16))
        {
            if (!filesByFieldId.TryGetValue(ff.Id.ToString(), out var file) || file.Length <= 0) continue;
            var maxMb = ff.UploadMaxSizeMb is > 0 and <= 100 ? ff.UploadMaxSizeMb!.Value : 10;
            var maxBytes = maxMb * 1024L * 1024L;
            if (file.Length > maxBytes) return BadRequest(new { message = $"حجم فایل فیلد «{ff.Label}» بیشتر از {maxMb}MB است." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".gif" };
            if (!allowed.Contains(ext)) return BadRequest(new { message = "فقط PDF یا تصویر مجاز است" });

            var safeName = $"{ff.Id}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var savePath = Path.Combine(uploadRoot, safeName);
            await using var stream = System.IO.File.Create(savePath);
            await file.CopyToAsync(stream, ct);
            values[ff.Id.ToString()] = $"/Formupload/{responderFolder}/{safeName}";
        }

        var fieldValues = form.Fields.Select(f => new FormFieldValueDto(
            f.Label,
            values.TryGetValue(f.Id.ToString(), out var v) ? v : ""
        )).ToList();

        var steps = BuildApprovalSteps(form.ApprovalWorkflowJson);
        var hasWorkflow = form.ApprovalEnabled && steps.Count > 0;
        if (hasWorkflow) steps[0].Status = "pending";

        db.FormSubmissions.Add(new FormSubmission
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            SubmitterName = link.ResponderFullName,
            SubmitterEmail = null,
            SubmittedAtUtc = DateTime.UtcNow,
            CurrentStepOrder = hasWorkflow ? 1 : 0,
            Status = hasWorkflow ? FormSubmissionStatus.InProgress : FormSubmissionStatus.Approved,
            FieldsJson = JsonSerializer.Serialize(fieldValues),
            StepsJson = hasWorkflow ? JsonSerializer.Serialize(steps) : null
        });
        link.UsedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        if (hasWorkflow)
        {
            var firstStep = steps.FirstOrDefault(x => x.Status == "pending");
            if (firstStep is not null)
                await SendInitialApprovalSmsAsync(firstStep.UserId, form.Title, ct);
        }

        return Ok(new { message = "فرم با موفقیت ثبت شد" });
    }

    private async Task SendInitialApprovalSmsAsync(Guid approverUserId, string formTitle, CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ApprovalReferralSmsEnabled) return;

        var approver = await db.Users.FirstOrDefaultAsync(x => x.Id == approverUserId, ct);
        if (approver is null || string.IsNullOrWhiteSpace(approver.PhoneNumber)) return;

        var msg =
            $"یک درخواست جدید از فرم «{formTitle}» برای تایید شما ثبت شد.\n" +
            $"لطفا به پنل مدیریت بخش تاییدیه‌ها مراجعه کنید.";
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        if (!string.IsNullOrWhiteSpace(adminBase))
            msg += $"\nلینک مستقیم: {adminBase}/admin/approvals";

        await smsSender.SendSmsAsync(new PorslineClone.Application.Contracts.SmsRequest(approver.PhoneNumber, msg), ct);
    }

    private static List<ApprovalStepDto> BuildApprovalSteps(string? workflowJson)
    {
        if (string.IsNullOrWhiteSpace(workflowJson)) return [];
        var workflow = JsonSerializer.Deserialize<List<WorkflowStepDto>>(workflowJson) ?? [];
        return workflow
            .OrderBy(x => x.Order)
            .Select((x, i) => new ApprovalStepDto
            {
                Id = Guid.NewGuid(),
                Order = i + 1,
                UserId = x.UserId,
                Status = i == 0 ? "pending" : "waiting",
                OnReject = x.OnReject is "continue" ? "continue" : "stop",
                Note = x.Note
            })
            .ToList();
    }
}

public class PublicSubmitRequest
{
    public string Code { get; set; } = "";
    public string? ValuesJson { get; set; }
}

public class PublicAccessOtpSendRequest
{
    public string Code { get; set; } = "";
    public string MobileNumber { get; set; } = "";
}

public class PublicAccessOtpVerifyRequest
{
    public string Code { get; set; } = "";
    public string MobileNumber { get; set; } = "";
    public string Otp { get; set; } = "";
}

