namespace PorslineClone.Infrastructure.Services.Sms;

/// <summary>درگاه entekhab با ارسال هم‌زمان/پشت‌سرهم پایدار نیست.</summary>
internal static class SmsGatewayThrottle
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastSendCompletedUtc = DateTime.MinValue;

    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var wait = MinInterval - (DateTime.UtcNow - _lastSendCompletedUtc);
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, ct);

            var result = await action();
            _lastSendCompletedUtc = DateTime.UtcNow;
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }
}
