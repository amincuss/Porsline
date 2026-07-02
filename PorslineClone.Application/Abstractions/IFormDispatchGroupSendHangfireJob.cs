namespace PorslineClone.Application.Abstractions;

public interface IFormDispatchGroupSendHangfireJob
{
    Task RunAsync(Guid jobId, CancellationToken cancellationToken = default);
}
