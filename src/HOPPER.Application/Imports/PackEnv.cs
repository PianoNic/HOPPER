using System.Text.Json;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    /// <summary>
    /// Reads the side a pack declares. Modrinth spells it two ways for the same idea - an
    /// <c>env</c> object per file in an .mrpack index, and <c>client_side</c>/<c>server_side</c> on
    /// a project from the API - and both use the same vocabulary, so one reader serves both.
    ///
    /// Absent or unrecognised is Both, which is what a mod with no declaration got before the side
    /// existed. Guessing narrower would silently withhold a jar somebody needs.
    /// </summary>
    public static class PackEnv
    {
        private const string Unsupported = "unsupported";

        public static ModSide Side(string? client, string? server)
        {
            var clientOut = string.Equals(client, Unsupported, StringComparison.OrdinalIgnoreCase);
            var serverOut = string.Equals(server, Unsupported, StringComparison.OrdinalIgnoreCase);

            // Unsupported on both sides is a contradiction the pack has to answer for, not
            // something to resolve by dropping the jar. Both keeps it visible in the dashboard,
            // where the admin can see it and decide.
            if (clientOut && serverOut)
                return ModSide.Both;

            if (clientOut)
                return ModSide.ServerOnly;

            if (serverOut)
                return ModSide.ClientOnly;

            return ModSide.Both;
        }

        /// <summary>Reads the <c>env</c> object of one .mrpack files[] entry.</summary>
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
