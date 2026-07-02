using Hangfire;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Api.HangfireJobs;

public interface IFormDispatchGroupSendEnqueue
{
    string Enqueue(Guid jobId);
    bool TryCancel(string? hangfireJobId);
}

public sealed class HangfireFormDispatchGroupSendEnqueue : IFormDispatchGroupSendEnqueue
{
    public string Enqueue(Guid jobId) =>
        BackgroundJob.Enqueue<IFormDispatchGroupSendHangfireJob>(
            x => x.RunAsync(jobId, CancellationToken.None));

    public bool TryCancel(string? hangfireJobId)
    {
        if (string.IsNullOrWhiteSpace(hangfireJobId)) return false;
        return BackgroundJob.Delete(hangfireJobId);
    }
}
