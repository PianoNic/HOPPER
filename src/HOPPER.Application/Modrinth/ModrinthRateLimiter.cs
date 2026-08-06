using System.Diagnostics;

namespace HOPPER.Application.Modrinth
{
    /// <summary>Process-wide token bucket for the Modrinth API. Registered as a singleton, because the
    /// limit Modrinth enforce is per IP and every scope in this process shares one.
    ///
    /// Capacity is their documented 300 per minute; the refill rate is deliberately lower at 240 per
    /// minute, leaving a fifth of the budget for the pack importer's version_files lookups, which go
    /// to the same host from the same address on a different client.</summary>
    public sealed class ModrinthRateLimiter
    {
        private const double Capacity = 300;
        private const double RefillPerSecond = 240d / 60d;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private double _tokens = Capacity;
        private long _lastRefill = Stopwatch.GetTimestamp();

        /// <summary>Acquires one token, waiting until one is available. Never throws on exhaustion:
        /// the caller is a browser request that should be slow rather than a search that fails.</summary>
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                TimeSpan wait;

                await _gate.WaitAsync(cancellationToken);
                try
                {
                    Refill();

                    if (_tokens >= 1)
                    {
                        _tokens -= 1;
                        return;
                    }

                    wait = TimeSpan.FromSeconds((1 - _tokens) / RefillPerSecond);
                }
                finally
                {
                    _gate.Release();
                }

                // Outside the gate: waiting while holding it would serialise every caller behind the
                // one that found the bucket empty.
                await Task.Delay(wait, cancellationToken);
            }
        }

        /// <summary>Removes tokens without waiting. Used as a brake when Modrinth's own counter says we
        /// are closer to the limit than the local bucket thinks.</summary>
        public void Drain(int count)
        {
            if (count <= 0)
                return;

            _gate.Wait();
            try
            {
                Refill();
                _tokens = Math.Max(0, _tokens - count);
            }
            finally
            {
                _gate.Release();
            }
        }

        private void Refill()
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - _lastRefill) / (double)Stopwatch.Frequency;
            _lastRefill = now;
            _tokens = Math.Min(Capacity, _tokens + elapsed * RefillPerSecond);
        }
    }
}
