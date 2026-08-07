using System.Text.Json;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public static class PackEnv
    {
        private const string Unsupported = "unsupported";

        public static ModSide Side(string? client, string? server)
        {
            var clientOut = string.Equals(client, Unsupported, StringComparison.OrdinalIgnoreCase);
            var serverOut = string.Equals(server, Unsupported, StringComparison.OrdinalIgnoreCase);

            if (clientOut && serverOut)
                return ModSide.Both;

            if (clientOut)
                return ModSide.ServerOnly;

            if (serverOut)
                return ModSide.ClientOnly;

            return ModSide.Both;
        }

        public static (string Client, string Server) Wire(ModSide side) => side switch
        {
            ModSide.ClientOnly => ("required", Unsupported),
            ModSide.ServerOnly => (Unsupported, "required"),
            _ => ("required", "required"),
        };

        public static ModSide SideOf(JsonElement file)
        {
            if (!file.TryGetProperty("env", out var env) || env.ValueKind != JsonValueKind.Object)
                return ModSide.Both;

            return Side(Text(env, "client"), Text(env, "server"));
        }

        private static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
