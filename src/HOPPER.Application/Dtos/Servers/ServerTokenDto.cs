namespace HOPPER.Application.Dtos.Servers
{
    /// <summary>The one shape that carries a server's bearer token. Kept apart from
    /// <see cref="ServerDto"/> on purpose: a token that rides along on every list response is a token
    /// in every browser cache and every log of a dashboard request, whereas this one is produced only
    /// by the endpoints that exist to produce it - reveal and rotate.</summary>
    public record ServerTokenDto
    {
        public required Guid ServerId { get; init; }

        /// <summary>64 lowercase hex characters, exactly as a client presents it after "Bearer ".</summary>
        public required string Token { get; init; }
    }
}
