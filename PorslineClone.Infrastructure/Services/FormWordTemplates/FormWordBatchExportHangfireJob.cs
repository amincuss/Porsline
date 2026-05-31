using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.FormWordTemplates;

public sealed class FormWordBatchExportHangfireJob(
    FormWordBatchExportService batchService,
    ILogger<FormWordBatchExportHangfireJob> logger) : IFormWordBatchExportHangfireJob
{
    public async Task RunAsync(Guid batchJobId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Form Word batch export started: {JobId}", batchJobId);
        try
        {
            await batchService.ExecuteBatchAsync(batchJobId, cancellationToken);
            logger.LogInformation("Form Word batch export finished: {JobId}", batchJobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Form Word batch export failed: {JobId}", batchJobId);
            throw;
        }
    }
}
