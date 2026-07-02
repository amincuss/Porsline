using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.FormDispatch;

public sealed class FormDispatchGroupSendHangfireJob(
    FormDispatchGroupSendService dispatchService,
    ILogger<FormDispatchGroupSendHangfireJob> logger) : IFormDispatchGroupSendHangfireJob
{
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Form dispatch group send started: {JobId}", jobId);
        try
        {
            await dispatchService.ExecuteGroupJobAsync(jobId, cancellationToken);
            logger.LogInformation("Form dispatch group send finished: {JobId}", jobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Form dispatch group send failed: {JobId}", jobId);
            throw;
        }
    }
}
