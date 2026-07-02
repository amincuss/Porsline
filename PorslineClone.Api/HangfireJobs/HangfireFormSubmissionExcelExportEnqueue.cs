using Hangfire;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Api.HangfireJobs;

public interface IFormSubmissionExcelExportEnqueue
{
    string Enqueue(Guid jobId);
}

public sealed class HangfireFormSubmissionExcelExportEnqueue : IFormSubmissionExcelExportEnqueue
{
    public string Enqueue(Guid jobId) =>
        BackgroundJob.Enqueue<IFormSubmissionExcelExportHangfireJob>(
            x => x.RunAsync(jobId, CancellationToken.None));
}
