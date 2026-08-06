using System.Net;

namespace HOPPER.Application.Modrinth
{
    /// <summary>Spends a token before every Modrinth request, reads their counter back as a brake, and
    /// waits out a 429 once before giving up.</summary>
    public sealed class ModrinthRateLimitHandler(ModrinthRateLimiter limiter) : DelegatingHandler
    {
        /// <summary>Below this many remaining requests, the local bucket is pulled down to match.</summary>
        private const int BrakeThreshold = 20;

        /// <summary>Their window is a minute; a reset longer than this is not something to sit on.</summary>
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(65);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await limiter.WaitAsync(cancellationToken);

            var response = await base.SendAsync(request, cancellationToken);
            ApplyBrake(response);

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return response;

            // A retry needs a fresh message - a sent one cannot be sent again - and a request with a
            // body cannot be cloned safely here. Every Modrinth call HOPPER makes is a GET.
            var retry = Clone(request);
            if (retry is null)
                return response;

            var delay = ResetDelay(response);
            response.Dispose();

            await Task.Delay(delay, cancellationToken);
            await limiter.WaitAsync(cancellationToken);

            var second = await base.SendAsync(retry, cancellationToken);
            ApplyBrake(second);
            return second;
        }

        // X-Ratelimit-Remaining is read as a brake, never as the source of truth. It was observed not
        // to decrement across five sequential /project/{id} calls - Cloudflare served them without
        // counting - while /search did decrement, so a limiter that trusted the header to always move
        // would let a burst through. The local bucket is what actually enforces the budget; this only
        // tightens it when their number is lower than ours.
        private void ApplyBrake(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("X-Ratelimit-Remaining", out var values))
                return;

            if (int.TryParse(values.FirstOrDefault(), out var remaining) && remaining <= BrakeThreshold)
                limiter.Drain(BrakeThreshold - remaining);
        }

        private static TimeSpan ResetDelay(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("X-Ratelimit-Reset", out var values)
                && int.TryParse(values.FirstOrDefault(), out var seconds)
                && seconds >= 0)
            {
                return TimeSpan.FromSeconds(Math.Min(seconds + 1, MaxRetryDelay.TotalSeconds));
            }

            return TimeSpan.FromSeconds(5);
        }

        private static HttpRequestMessage? Clone(HttpRequestMessage request)
        {
            if (request.Content is not null || request.RequestUri is null)
                return null;

            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            return clone;
        }
    }
}
