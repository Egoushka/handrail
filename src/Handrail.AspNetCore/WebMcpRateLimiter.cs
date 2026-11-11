using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Handrail.AspNetCore;

internal sealed class WebMcpRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string>? _limiter;

    public WebMcpRateLimiter(IOptions<WebMcpOptions> options)
    {
        var permits = options.Value.RateLimitPerMinute;
        if (permits > 0)
        {
            _limiter = PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
        }
    }

    public bool TryAcquire(string key)
    {
        if (_limiter is null)
        {
            return true;
        }

        using var lease = _limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter?.Dispose();
}
