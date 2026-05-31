namespace PorslineClone.Application.Abstractions;

public interface IFormWordBatchExportHangfireJob
{
    Task RunAsync(Guid batchJobId, CancellationToken cancellationToken = default);
}
