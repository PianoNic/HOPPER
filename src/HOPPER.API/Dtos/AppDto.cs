namespace HOPPER.API.Dtos
{
    /// <summary>What the Angular app needs before it can log anyone in. API-local and positional,
    /// following KRINT's NodeDtos precedent for contracts that never leave the API layer.
    /// Deliberately carries no client token: that secret lives in server configuration and is handed
    /// to players out of band, never over HTTP.</summary>
    public record AppDto(
        string Authority,
        string ClientId,
        string RedirectUri,
        string PostLogoutRedirectUri,
        string Scope,
        string Version);
}
