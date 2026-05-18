using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

internal static class WordOpenXmlImageHelper
{
    private static uint _drawingIdCounter;

    public static void ResetDrawingIds() => _drawingIdCounter = 0;

    public static long PixelsToEmu(int px) => px * 9525L;

    public static void ReplaceParagraphWithImage(
        MainDocumentPart mainPart,
        Paragraph paragraph,
        byte[] imageBytes,
        string fileExtension,
        int widthPx,
        string imageName)
    {
        var (imgW, imgH) = ImageDimensionReader.TryRead(imageBytes) ?? (widthPx, widthPx);
        var cx = PixelsToEmu(widthPx);
        var cy = imgW > 0 ? (long)((double)cx * imgH / imgW) : cx;

        var pPr = paragraph.GetFirstChild<ParagraphProperties>()?.CloneNode(true) as ParagraphProperties;
        paragraph.RemoveAllChildren();
        if (pPr is not null)
            paragraph.AppendChild(pPr);

        var imagePart = mainPart.AddImagePart(ResolveImagePartType(fileExtension));
        using (var stream = new MemoryStream(imageBytes))
            imagePart.FeedData(stream);

        var relId = mainPart.GetIdOfPart(imagePart);
        paragraph.AppendChild(new Run(CreateDrawing(relId, SanitizeImageName(imageName), cx, cy)));
    }

    private static string SanitizeImageName(string name)
    {
        var safe = new string((name ?? "image").Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "image" : safe;
    }

    private static PartTypeInfo ResolveImagePartType(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => ImagePartType.Jpeg,
        ".gif" => ImagePartType.Gif,
        ".png" => ImagePartType.Png,
        _ => ImagePartType.Png
    };

    private static Drawing CreateDrawing(string relationshipId, string name, long cx, long cy)
    {
        var id = (uint)Interlocked.Increment(ref _drawingIdCounter);
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = id, Name = $"{name}.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip
                                {
                                    Embed = relationshipId,
                                    CompressionState = A.BlipCompressionValues.Print
                                },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = cx, Cy = cy }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });
    }
}
