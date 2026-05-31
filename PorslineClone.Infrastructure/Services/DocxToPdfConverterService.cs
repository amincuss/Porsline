using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services;

public class DocxToPdfConverterService(ILibreOfficeDocumentService libreOffice) : IDocxToPdfConverter
{
    public bool IsAvailable => libreOffice.IsAvailable;

    public string? TryConvert(string docxFullPath) => libreOffice.TryConvertToPdf(docxFullPath);
}
