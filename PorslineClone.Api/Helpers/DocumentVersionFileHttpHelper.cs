using Microsoft.AspNetCore.Mvc;
using PorslineClone.Api.Http;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Api.Helpers;

public static class DocumentVersionFileHttpHelper
{
    public static async Task<IActionResult?> TryServePhysicalAsync(
        IDocumentVersionFileAccess files,
        DocumentVersion version,
        string contentType,
        string? downloadName,
        bool inline,
        HttpResponse response,
        CancellationToken ct)
    {
        if (!files.FileExists(version))
            return null;

        var local = await files.OpenLocalPathAsync(version, ct);
        if (local.DeleteWhenDisposed)
        {
            var pathToDelete = local.Path;
            response.OnCompleted(() =>
            {
                try
                {
                    if (File.Exists(pathToDelete))
                        File.Delete(pathToDelete);
                }
                catch
                {
                    // ignore
                }

                return Task.CompletedTask;
            });
        }

        if (inline && !string.IsNullOrWhiteSpace(downloadName))
            ContentDispositionHelper.SetInline(response, downloadName);
        else if (!string.IsNullOrWhiteSpace(downloadName))
            ContentDispositionHelper.SetAttachment(response, downloadName);

        return new PhysicalFileResult(local.Path, contentType)
        {
            FileDownloadName = inline ? null : downloadName,
            EnableRangeProcessing = true,
        };
    }
}
