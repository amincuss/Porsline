using Hangfire;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Api.HangfireJobs;

public sealed class HangfireContractIndexEnqueue : IContractIndexEnqueue
{
    public void EnqueueExtractAndIndex(Guid contractId, bool force = false)
    {
        BackgroundJob.Enqueue<IContractExtractAndIndexJob>(
            x => x.ExtractAndIndexAsync(contractId, force, CancellationToken.None));
    }
}
