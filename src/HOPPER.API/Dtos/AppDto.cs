namespace HOPPER.API.Dtos
{
    public record AppDto(
        string Authority,
        string ClientId,
        string RedirectUri,
        string PostLogoutRedirectUri,
        string Scope,
        string Version);
}
