using System.Reflection;

namespace HOPPER.Application
{
    public static class HopperVersion
    {
        public static string Current { get; } =
            typeof(HopperVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "0.0.0";
    }
}
