using Microsoft.Extensions.Configuration;

public static class TestLimits
{
    public const long MaxBytes = long.MaxValue;

    public static IConfiguration Config { get; } = new ConfigurationBuilder().Build();

    public static IConfiguration ConfigWith(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();
}
