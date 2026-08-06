using System.Net;

namespace HOPPER.Application.Modrinth
{
    public sealed class ModrinthRateLimitHandler(ModrinthRateLimiter limiter) : DelegatingHandler
    {
        private const int BrakeThreshold = 20;

        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(65);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await limiter.WaitAsync(cancellationToken);

            var response = await base.SendAsync(request, cancellationToken);
            ApplyBrake(response);

            if (response.StatusCode != HttpStatusCode.TooManyRequests)
                return response;

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
