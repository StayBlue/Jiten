using System.Collections.Concurrent;

namespace Jiten.Api.Services;

public class SrsDebounceService : ISrsDebounceService
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(1f);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5f);

    private readonly ConcurrentDictionary<string, DateTime> _lastOperationTimes = new();
    private DateTime _lastCleanup = DateTime.UtcNow;
    private readonly Lock _cleanupLock = new();

    public bool TryAcquire(string userId, int wordId, byte readingIndex)
    {
        var key = $"{userId}:{wordId}:{readingIndex}";
        var now = DateTime.UtcNow;

        CleanupIfNeeded(now);

        var lastTime = _lastOperationTimes.GetOrAdd(key, DateTime.MinValue);

        if (now - lastTime < DebounceWindow)
        {
            return false;
        }

        _lastOperationTimes[key] = now;
        return true;
    }

    private void CleanupIfNeeded(DateTime now)
    {
        if (now - _lastCleanup < CleanupInterval)
            return;

        lock (_cleanupLock)
        {
            if (now - _lastCleanup < CleanupInterval)
                return;

            var cutoff = now - DebounceWindow - TimeSpan.FromSeconds(1);
            var keysToRemove = _lastOperationTimes
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _lastOperationTimes.TryRemove(key, out _);
            }

            _lastCleanup = now;
        }
    }
}
