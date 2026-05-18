using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Options;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace PorslineClone.Infrastructure.Services;

public sealed record ContractSignatoryStamp(string SignatureRelativePath, string FullName, string? PositionTitle);

/// <summary>درج امضای تأییدکنندگان و ذخیره نسخه PDF امضاشده</summary>
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

    public bool TryStampAndSavePdf(
        string sourceRelativePath,
        string signatureRelativePath,
        string fullName,
        string? positionTitle,
        int stackIndex,
        out string? newRelativePath)
    {
        newRelativePath = null;
        if (!_options.Enabled) return false;

        var sourceFull = UserSignatureStorageService.ResolveFullPath(env, sourceRelativePath);
        if (!File.Exists(sourceFull)) return false;

        var sigFull = UserSignatureStorageService.ResolveFullPath(env, signatureRelativePath);
        if (!File.Exists(sigFull)) return false;

        var ext = Path.GetExtension(sourceFull).ToLowerInvariant();
        try
        {
            if (ext == ".pdf")
            {
                newRelativePath = StampPdf(sourceFull, sigFull, fullName, positionTitle, stackIndex);
                return true;
            }

            if (ext == ".docx")
            {
                var docxRel = StampDocx(sourceFull, sigFull, fullName, positionTitle);
                var docxFull = UserSignatureStorageService.ResolveFullPath(env, docxRel);
                var pdfRel = TryConvertDocxToPdfRelative(docxFull);
                newRelativePath = pdfRel ?? docxRel;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Contract stamp failed for {Path}", sourceRelativePath);
            return false;
        }
    }

    public static bool IsSignedDocumentPath(string? relativePath) =>
        !string.IsNullOrWhiteSpace(relativePath)
        && relativePath.Contains("_signed_", StringComparison.OrdinalIgnoreCase);

    /// <summary>از نسخه اصلی، PDF با تمام امضاهای تأییدشده می‌سازد.</summary>
    public bool TryRebuildSignedPdf(
        string originalRelativePath,
        IReadOnlyList<ContractSignatoryStamp> signatories,
        out string? newRelativePath)
    {
        newRelativePath = null;
        if (!_options.Enabled || signatories.Count == 0) return false;

        var sourceFull = UserSignatureStorageService.ResolveFullPath(env, originalRelativePath);
        if (!File.Exists(sourceFull)) return false;

        var ext = Path.GetExtension(sourceFull).ToLowerInvariant();
        try
        {
            string pdfFull;
            if (ext == ".pdf")
            {
                pdfFull = CopyToWorkPdf(sourceFull);
            }
            else if (ext == ".docx")
            {
                var converted = TryConvertDocxToPdfRelative(sourceFull);
                if (converted is null) return false;
                pdfFull = UserSignatureStorageService.ResolveFullPath(env, converted);
                if (!File.Exists(pdfFull)) return false;
            }
            else
            {
                return false;
            }

            using var document = PdfReader.Open(pdfFull, PdfDocumentOpenMode.Modify);
            var page = document.Pages[^1];
            for (var i = 0; i < signatories.Count; i++)
            {
                var s = signatories[i];
                var sigFull = UserSignatureStorageService.ResolveFullPath(env, s.SignatureRelativePath);
                if (!File.Exists(sigFull)) continue;
                DrawStampOnPage(page, sigFull, s.FullName, s.PositionTitle, i);
            }

            var dir = Path.GetDirectoryName(sourceFull)!;
            var baseName = Path.GetFileNameWithoutExtension(sourceFull);
            var destFull = Path.Combine(dir, $"{baseName}_signed_{DateTime.UtcNow:yyyyMMddHHmmssfff}.pdf");
            document.Save(destFull);
            TryDeleteQuiet(pdfFull != sourceFull ? pdfFull : null);
            newRelativePath = ToRelative(destFull);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Contract rebuild stamp failed for {Path}", originalRelativePath);
            return false;
        }
    }

    private static void TryDeleteQuiet(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { /* ignore */ }
    }

    private static string CopyToWorkPdf(string sourceFull)
    {
        var dir = Path.GetDirectoryName(sourceFull)!;
        var work = Path.Combine(dir, $"_work_{Guid.NewGuid():N}.pdf");
        File.Copy(sourceFull, work, overwrite: true);
        return work;
    }

    private void DrawStampOnPage(PdfPage page, string sigFull, string fullName, string? positionTitle, int stackIndex)
    {
        var y = ComputeStampY(page, stackIndex);
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        using var img = XImage.FromFile(sigFull);
        gfx.DrawImage(img, Margin, y, SigWidth, SigHeight);
        var font = new XFont("Arial", 10, XFontStyle.Bold);
        var subFont = new XFont("Arial", 9, XFontStyle.Regular);
        var nameY = y + SigHeight + 6;
        gfx.DrawString(fullName, font, XBrushes.DarkSlateGray, Margin, nameY);
        if (!string.IsNullOrWhiteSpace(positionTitle))
            gfx.DrawString(positionTitle, subFont, XBrushes.Gray, Margin, nameY + 14);
    }

    private string StampPdf(string sourceFull, string sigFull, string fullName, string? positionTitle, int stackIndex)
    {
        var dir = Path.GetDirectoryName(sourceFull)!;
        var baseName = Path.GetFileNameWithoutExtension(sourceFull);
        var destFull = Path.Combine(dir, $"{baseName}_signed_{DateTime.UtcNow:yyyyMMddHHmmssfff}.pdf");

        using var document = PdfReader.Open(sourceFull, PdfDocumentOpenMode.Modify);
        var page = document.Pages[^1];
        DrawStampOnPage(page, sigFull, fullName, positionTitle, stackIndex);
        document.Save(destFull);
        return ToRelative(destFull);
    }

    private static double ComputeStampY(PdfPage page, int stackIndex)
    {
        var fromBottom = Margin + SigHeight + 28 + stackIndex * BlockHeight;
        if (fromBottom <= page.Height.Point - Margin)
            return page.Height.Point - fromBottom;

        return Margin;
    }

    private string StampDocx(string sourceFull, string sigFull, string fullName, string? positionTitle)
    {
        var dir = Path.GetDirectoryName(sourceFull)!;
        var baseName = Path.GetFileNameWithoutExtension(sourceFull);
        var destFull = Path.Combine(dir, $"{baseName}_signed_{DateTime.UtcNow:yyyyMMddHHmmssfff}.docx");
        File.Copy(sourceFull, destFull, overwrite: true);

        using var doc = WordprocessingDocument.Open(destFull, true);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.AppendChild(new Paragraph());
        var imagePart = doc.MainDocumentPart.AddImagePart(ImagePartType.Png);
        using (var stream = File.OpenRead(sigFull))
            imagePart.FeedData(stream);
        var relId = doc.MainDocumentPart.GetIdOfPart(imagePart);

        body.AppendChild(CreateImageParagraph(relId, fullName));
        body.AppendChild(new Paragraph(new Run(new Text(fullName) { Space = SpaceProcessingModeValues.Preserve })));
        if (!string.IsNullOrWhiteSpace(positionTitle))
            body.AppendChild(new Paragraph(new Run(new Text(positionTitle) { Space = SpaceProcessingModeValues.Preserve })));

        doc.MainDocumentPart.Document.Save();
        return ToRelative(destFull);
    }

    private string? TryConvertDocxToPdfRelative(string docxFull)
    {
        var pdfFull = pdfConverter.TryConvert(docxFull);
        if (pdfFull is null)
        {
            logger.LogInformation("PDF conversion unavailable; DOCX kept at {Path}", docxFull);
            return null;
        }

        var signedPdf = Path.Combine(
            Path.GetDirectoryName(docxFull)!,
            $"{Path.GetFileNameWithoutExtension(docxFull)}_aspdf_{DateTime.UtcNow:yyyyMMddHHmmssfff}.pdf");
        File.Copy(pdfFull, signedPdf, overwrite: true);
        return ToRelative(signedPdf);
    }

    private static Paragraph CreateImageParagraph(string relationshipId, string name)
    {
        const long cx = 1400000L;
        const long cy = 550000L;
        var element =
            new DocumentFormat.OpenXml.Wordprocessing.Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = cx, Cy = cy },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = 1U, Name = name },
                    new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = 0U, Name = "Signature.png" },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip { Embed = relationshipId },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset { X = 0L, Y = 0L },
                                        new A.Extents { Cx = cx, Cy = cy }),
                                    new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                        ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = 0U,
                    DistanceFromBottom = 0U,
                    DistanceFromLeft = 0U,
                    DistanceFromRight = 0U
                });

        return new Paragraph(new Run(element));
    }

    private string ToRelative(string fullPath)
    {
        var root = env.ContentRootPath ?? Directory.GetCurrentDirectory();
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        return rel.StartsWith('/') ? rel : "/" + rel;
    }
}
