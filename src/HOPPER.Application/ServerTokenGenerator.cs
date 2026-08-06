using System.Security.Cryptography;

namespace HOPPER.Application
{
    /// <summary>Mints the bearer token a server's clients present.</summary>
    public static class ServerTokenGenerator
    {
        /// <summary>256 bits of CSPRNG output as 64 lowercase hex characters.
        ///
        /// Hex rather than base64 on purpose: the value is written into a java.util.Properties file
        /// inside the generated jar and pasted into .env files, and hex contains no character that
        /// either format treats specially - no '=', '+', '/' or ':' to escape or truncate on.</summary>
        public static string New() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }
}
