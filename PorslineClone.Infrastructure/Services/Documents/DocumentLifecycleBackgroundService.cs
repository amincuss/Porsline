using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>پردازش خودکار آرشیو، هشدار انقضا و حذف اسناد (با رعایت Legal Hold).</summary>
public sealed class DocumentLifecycleBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DocumentLifecycleBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);

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

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalHours = 6;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<DocumentLifecycleService>();
                var settings = await service.GetOrCreateSettingsAsync(stoppingToken);
                intervalHours = Math.Clamp(settings.ProcessIntervalHours, 1, 168);
                var processed = await service.ProcessDueDocumentsAsync(stoppingToken);
                if (processed > 0)
                    logger.LogInformation("Document lifecycle job processed {Count} actions", processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Document lifecycle background job failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
