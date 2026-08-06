namespace HOPPER.API.Auth
{
    public static class ClientTokenDefaults
    {
        public const string AuthenticationScheme = "ClientToken";

        /// <summary>Claim carrying the id of the Server the presented token resolved to. This is the
        /// only place a client's tenant is decided; every client-facing endpoint reads it rather than
        /// accepting a server from the URL or the body.</summary>
        public const string ServerIdClaim = "hopper:server_id";
    }
}
