using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.ContractTemplates;
using PorslineClone.Infrastructure.Options;
using PorslineClone.Infrastructure.Services.ContractTemplates;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace PorslineClone.Infrastructure.Services;

public sealed record ContractSignatoryStamp(string SignatureRelativePath, string FullName, string? PositionTitle);

/// <summary>بازنویسی امضا روی همان فایل قرارداد (بدون ساخت مسیر _signed_ جدید برای DOCX).</summary>
public class ContractApprovalStampService(
    IHostEnvironment env,
    ILogger<ContractApprovalStampService> logger,
    IOptions<ContractSignatureOptions> signatureOptions,
    IDocxToPdfConverter pdfConverter)
{
    private readonly ContractSignatureOptions _options = signatureOptions.Value;

    private const double SigWidth = 140;
    private const double SigHeight = 55;
    private const double Margin = 36;
    private const double BlockHeight = 78;

    public static bool IsSignedDocumentPath(string? relativePath) =>
        !string.IsNullOrWhiteSpace(relativePath)
        && relativePath.Contains("_signed_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// از نسخهٔ pristine کپی می‌گیرد، امضاها را در workRelativePath می‌نویسد (همان فایل کاری — بازنویسی مستقیم).
    /// </summary>
    public bool TryRewriteContractFile(
        string workRelativePath,
        string pristineRelativePath,
        IReadOnlyList<ContractSignatureSlot> slots,
        out string? resultRelativePath)
    {
        resultRelativePath = workRelativePath;

        if (!_options.Enabled || slots.Count == 0)
        {
            logger.LogWarning("Contract signatures disabled or no slots");
            return false;
        }

        var workFull = ResolveFullPath(workRelativePath);
        var pristineFull = ResolveFullPath(pristineRelativePath);

        if (!File.Exists(pristineFull))
        {
            logger.LogWarning("Pristine contract file missing: {Path}", pristineRelativePath);
            return false;
        }

        var ext = Path.GetExtension(pristineFull).ToLowerInvariant();
        if (ext is not ".docx" and not ".doc")
            return TryRewritePdfFromPristine(pristineFull, workFull, ext, slots);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(workFull)!);
            File.Copy(pristineFull, workFull, overwrite: true);

            var keysInDoc = ContractSignatureDocumentWriter.ScanPlaceholderKeys(workFull);
            logger.LogInformation(
                "Signature rewrite: work={Work}, pristine={Pristine}, slots={Slots}, placeholdersInDoc=[{Keys}]",
                workRelativePath,
                pristineRelativePath,
                slots.Count,
                string.Join(", ", keysInDoc));

            var result = ContractSignatureDocumentWriter.ApplySignatures(workFull, slots);

            if (result.InsertedInPlaceholder == 0)
            {
                logger.LogError(
                    "No signature inserted into {Work}. Placeholders in doc: [{Keys}], required slot keys: [{SlotKeys}]",
                    workRelativePath,
                    string.Join(", ", keysInDoc),
                    string.Join(", ", slots.Select(s => ContractTemplateSystemFields.NormalizeKey(s.PlaceholderKey))));
                return false;
            }

            if (result.MissingKeys.Count > 0)
            {
                logger.LogWarning(
                    "No matching {{key}} in Word for: [{Keys}]. Add exact placeholders or align template field keys.",
                    string.Join(", ", result.MissingKeys));
            }

            logger.LogInformation(
                "Signatures written to {Work}: {Count} placeholder(s) filled (strict key match)",
                workRelativePath,
                result.InsertedInPlaceholder);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rewrite signatures on {Work}", workRelativePath);
            return false;
        }
    }

    public bool TryRebuildSignedDocument(
        string originalRelativePath,
        IReadOnlyList<ContractSignatureSlot> slots,
        out string? newRelativePath)
        => TryRewriteContractFile(originalRelativePath, originalRelativePath, slots, out newRelativePath);

    public bool TryRebuildSignedPdf(
        string originalRelativePath,
        IReadOnlyList<ContractSignatoryStamp> signatories,
        IReadOnlyList<string>? signaturePlaceholderKeys,
        out string? newRelativePath)
    {
        var slots = BuildSlots(signatories, signaturePlaceholderKeys);
        return TryRebuildSignedDocument(originalRelativePath, slots, out newRelativePath);
    }

    private List<ContractSignatureSlot> BuildSlots(
        IReadOnlyList<ContractSignatoryStamp> signatories,
        IReadOnlyList<string>? keys)
    {
        var slots = new List<ContractSignatureSlot>();
        for (var i = 0; i < signatories.Count; i++)
        {
            var s = signatories[i];
            var sigFull = UserSignatureStorageService.ResolveFullPath(env, s.SignatureRelativePath);
            if (!File.Exists(sigFull))
                continue;

            var ext = Path.GetExtension(sigFull);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            var key = keys is not null && i < keys.Count
                ? keys[i]
                : $"sign_{i + 1}";

            slots.Add(new ContractSignatureSlot(
                WorkflowOrder: i + 1,
                PlaceholderKey: key,
                ImageBytes: File.ReadAllBytes(sigFull),
                ImageExtension: ext,
                ApproverFullName: s.FullName,
                PositionTitle: s.PositionTitle));
        }

        return slots;
    }

    private bool TryRewritePdfFromPristine(
        string pristineFull,
        string workFull,
        string ext,
        IReadOnlyList<ContractSignatureSlot> slots)
    {
        string pdfWork;
        if (ext == ".pdf")
        {
            pdfWork = workFull.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? workFull
                : Path.ChangeExtension(workFull, ".pdf");
            File.Copy(pristineFull, pdfWork, overwrite: true);
        }
        else
        {
            var converted = pdfConverter.TryConvert(pristineFull);
            if (converted is null)
                return false;
            pdfWork = workFull.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                ? workFull
                : Path.ChangeExtension(workFull, ".pdf");
            File.Copy(converted, pdfWork, overwrite: true);
        }

        using var document = PdfReader.Open(pdfWork, PdfDocumentOpenMode.Modify);
        var page = document.Pages[^1];
        var stackIndex = 0;

        foreach (var slot in slots.OrderBy(s => s.WorkflowOrder))
        {
            var tempSig = Path.Combine(Path.GetTempPath(), $"sig_{Guid.NewGuid():N}{slot.ImageExtension}");
            try
            {
                File.WriteAllBytes(tempSig, slot.ImageBytes);
                DrawStampOnPage(page, tempSig, slot.ApproverFullName, slot.PositionTitle, stackIndex++);
            }
            finally
            {
                TryDeleteQuiet(tempSig);
            }
        }

        document.Save(pdfWork);
        return true;
    }

    private void DrawStampOnPage(PdfPage page, string sigFull, string fullName, string? positionTitle, int stackIndex)
    {
        var y = ComputeStampY(page, stackIndex);
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        using var img = XImage.FromFile(sigFull);
        gfx.DrawImage(img, Margin, y, SigWidth, SigHeight);
        var font = new XFont("Arial", 10, XFontStyle.Bold);
        var subFont = new XFont("Arial", 9, XFontStyle.Regular);
        var lineY = y + SigHeight + 6;
        if (!string.IsNullOrWhiteSpace(positionTitle))
        {
            gfx.DrawString(positionTitle, subFont, XBrushes.Gray, Margin, lineY);
            lineY += 14;
        }

        if (!string.IsNullOrWhiteSpace(fullName))
            gfx.DrawString(fullName, font, XBrushes.DarkSlateGray, Margin, lineY);
    }

    private static double ComputeStampY(PdfPage page, int stackIndex)
    {
        var fromBottom = Margin + SigHeight + 28 + stackIndex * BlockHeight;
        return fromBottom <= page.Height.Point - Margin
            ? page.Height.Point - fromBottom
            : Margin;
    }

    private static void TryDeleteQuiet(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { /* ignore */ }
    }

    /// <summary>یک کپی فقط-خواندنی از سند بدون امضا (یک‌بار ساخته می‌شود).</summary>
    public string EnsurePristineBackupRelative(string sourceRelative)
    {
        var sourceFull = ResolveFullPath(sourceRelative);
        if (!File.Exists(sourceFull))
            return sourceRelative;

        var dir = Path.GetDirectoryName(sourceFull)!;
        var baseName = Path.GetFileNameWithoutExtension(sourceFull);
        var ext = Path.GetExtension(sourceFull);
        var pristineFull = Path.Combine(dir, $"{baseName}_pristine{ext}");

        if (!File.Exists(pristineFull))
            File.Copy(sourceFull, pristineFull, overwrite: false);

        return ToRelative(pristineFull);
    }

    private string ResolveFullPath(string relativePath)
        => UserSignatureStorageService.ResolveFullPath(env, relativePath);

    private string ToRelative(string fullPath)
    {
        var root = env.ContentRootPath ?? Directory.GetCurrentDirectory();
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return rel.StartsWith('/') ? rel : "/" + rel;
    }
}
