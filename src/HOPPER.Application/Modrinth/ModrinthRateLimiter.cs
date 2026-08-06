using System.Diagnostics;

namespace HOPPER.Application.Modrinth
{
    public sealed class ModrinthRateLimiter
    {
        private const double Capacity = 300;
        private const double RefillPerSecond = 240d / 60d;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private double _tokens = Capacity;
        private long _lastRefill = Stopwatch.GetTimestamp();

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

                await Task.Delay(wait, cancellationToken);
            }
        }

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
