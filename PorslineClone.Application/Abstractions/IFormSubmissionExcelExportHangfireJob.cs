namespace PorslineClone.Application.Abstractions;

public interface IFormSubmissionExcelExportHangfireJob
{
    Task RunAsync(Guid jobId, CancellationToken cancellationToken = default);
}
