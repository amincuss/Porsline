using Hangfire;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Services.FormWordTemplates;

namespace PorslineClone.Api.HangfireJobs;

public interface IFormWordBatchExportEnqueue
{
    string Enqueue(Guid batchJobId);
}

public sealed class HangfireFormWordBatchExportEnqueue : IFormWordBatchExportEnqueue
{
    public string Enqueue(Guid batchJobId) =>
        BackgroundJob.Enqueue<IFormWordBatchExportHangfireJob>(
            x => x.RunAsync(batchJobId, CancellationToken.None));
}
