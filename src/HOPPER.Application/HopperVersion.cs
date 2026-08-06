using System.Reflection;

namespace HOPPER.Application
{
    /// <summary>The running build's version string, read once from the assembly.
    ///
    /// /application.properties at the repo root is the single source of truth; src/Directory.Build.props
    /// XmlPeeks it into AssemblyInformationalVersion at build time. SourceLink may append "+&lt;commit&gt;",
    /// which is stripped here so what ships is a clean semver.
    ///
    /// It lives in Application rather than next to the controller that first needed it because the
    /// Modrinth User-Agent has to carry the same string: Modrinth identify callers by agent and a
    /// version that drifts from what /api/app reports would make a blocked deployment impossible to
    /// correlate with a release.</summary>
    public static class HopperVersion
    {
        public static string Current { get; } =
            typeof(HopperVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "0.0.0";
    }
}
