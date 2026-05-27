using Microsoft.Extensions.Hosting;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormApprovalSignatureHelper
{
    public static void CaptureSignatureOnApprove(ApprovalStepDto step, AppUser? user, string? positionTitle = null)
    {
        if (user is null)
            return;

        if (!string.IsNullOrWhiteSpace(user.FirstName))
            step.UserFirstName = user.FirstName.Trim();
        if (!string.IsNullOrWhiteSpace(user.LastName))
            step.UserLastName = user.LastName.Trim();
        if (!string.IsNullOrWhiteSpace(positionTitle))
            step.UserPositionTitle = positionTitle.Trim();
        if (user.Gender is { } gender)
            step.UserGender = (int)gender;

        if (string.IsNullOrWhiteSpace(user.SignatureImagePath))
            return;
        step.SignatureImagePath = user.SignatureImagePath;
        step.SignatureDisplayDegree = UserSignatureDisplaySize.NormalizeDegree(user.SignatureDisplayDegree);
    }

    public static void EnrichApproverIdentityFromProfile(
        ApprovalStepDto step,
        string? firstName,
        string? lastName,
        string? positionTitle,
        UserGender? gender = null)
    {
        if (string.IsNullOrWhiteSpace(step.UserFirstName) && !string.IsNullOrWhiteSpace(firstName))
            step.UserFirstName = firstName.Trim();
        if (string.IsNullOrWhiteSpace(step.UserLastName) && !string.IsNullOrWhiteSpace(lastName))
            step.UserLastName = lastName.Trim();
        if (string.IsNullOrWhiteSpace(step.UserPositionTitle) && !string.IsNullOrWhiteSpace(positionTitle))
            step.UserPositionTitle = positionTitle.Trim();
        if (step.UserGender is null && gender is not null)
            step.UserGender = (int)gender;
    }

    public static string? ValidateApproverSignature(AppUser? user)
    {
        if (user is null || string.IsNullOrWhiteSpace(user.SignatureImagePath))
            return "تصویر امضا در پروفایل شما ثبت نشده است. از منوی پروفایل امضا را آپلود کنید.";
        return null;
    }

    /// <summary>مراحل تأییدشدهٔ قدیمی که قبل از ذخیرهٔ مسیر امضا در JSON بودند — از پروفایل فعلی تأییدکننده پر می‌شود.</summary>
    public static void BackfillApprovedStepSignatures(
        IEnumerable<ApprovalStepDto> steps,
        IReadOnlyDictionary<Guid, (string? Path, int Degree)> userSignatures)
    {
        foreach (var step in steps)
        {
            if (step.Status != "approved" || !string.IsNullOrWhiteSpace(step.SignatureImagePath))
                continue;
            if (!userSignatures.TryGetValue(step.UserId, out var sig) || string.IsNullOrWhiteSpace(sig.Path))
                continue;
            step.SignatureImagePath = sig.Path;
            step.SignatureDisplayDegree ??= UserSignatureDisplaySize.NormalizeDegree(sig.Degree);
        }
    }

    public static void EnrichSignatureUrls(
        IEnumerable<ApprovalStepDto> steps,
        Func<ApprovalStepDto, string?> urlFactory)
    {
        foreach (var step in steps)
        {
            if (step.Status != "approved" || string.IsNullOrWhiteSpace(step.SignatureImagePath))
            {
                step.SignatureUrl = null;
                continue;
            }
            step.SignatureUrl = urlFactory(step);
        }
    }

    public static bool TryResolveSignatureFile(IHostEnvironment env, string? relativePath, out string fullPath)
    {
        fullPath = UserSignatureStorageService.ResolveFullPath(env, relativePath);
        return !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath);
    }
}
