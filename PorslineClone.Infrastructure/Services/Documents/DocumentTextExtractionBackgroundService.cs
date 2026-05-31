using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>
/// پردازش ناهمگام استخراج متن — Channel + polling DB برای موارد از دست‌رفته.
/// Hangfire: برای چند سرور / UI داشبورد retry؛ برای تک‌نود BackgroundService کافی است.
/// </summary>
public sealed class DocumentTextExtractionBackgroundService(
    DocumentTextExtractionQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentTextExtractionBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var channelTask = DrainChannelAsync(stoppingToken);
        var pollTask = PollDatabaseAsync(stoppingToken);
        try
        {
            await Task.WhenAll(channelTask, pollTask);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown عادی
        }
    }

    private async Task DrainChannelAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var versionId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IDocumentTextExtractionProcessor>();
                    await processor.ProcessVersionAsync(versionId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Channel document text job failed version={VersionId}", versionId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown عادی
        }
    }

    private async Task PollDatabaseAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var processor = scope.ServiceProvider.GetRequiredService<IDocumentTextExtractionProcessor>();

                var pending = await db.DocumentVersionTexts
                    .AsNoTracking()
                    .Where(x => x.ProcessingStatus == DocumentTextProcessingStatus.Pending && x.AttemptCount < 3)
                    .OrderBy(x => x.UpdatedAtUtc)
                    .Select(x => x.DocumentVersionId)
                    .Take(5)
                    .ToListAsync(stoppingToken);

                foreach (var id in pending)
                    await processor.ProcessVersionAsync(id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document text extraction poll failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
