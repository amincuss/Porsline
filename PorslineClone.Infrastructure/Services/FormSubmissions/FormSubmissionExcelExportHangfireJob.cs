using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.FormSubmissions;

public sealed class FormSubmissionExcelExportHangfireJob(
    FormSubmissionExcelExportService exportService,
    ILogger<FormSubmissionExcelExportHangfireJob> logger) : IFormSubmissionExcelExportHangfireJob
{
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Form submission Excel export started: {JobId}", jobId);
        try
        {
            await exportService.ExecuteBatchAsync(jobId, cancellationToken);
            logger.LogInformation("Form submission Excel export finished: {JobId}", jobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Form submission Excel export failed: {JobId}", jobId);
            throw;
        }
    }
}
