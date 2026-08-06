using System.Security.Cryptography;

namespace HOPPER.Application
{
    public static class ServerTokenGenerator
    {
        public static string New() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
    }
}
