using Xunit;

namespace ArbitrageScanner.Tests.Helpers;

/// <summary>
/// DataService.ProxiesList/watch* dictionaries are process-wide static state (by production design).
/// Tests that touch them via LoadProxiesAsync/StartOperation must run sequentially relative to each
/// other, or a concurrently-running test can replace the shared proxy pool mid-assertion.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedProxyPoolCollection
{
    public const string Name = "SharedProxyPool";

    private SharedProxyPoolCollection() { }
}

/// <summary>
/// TELEGRAM_TOKEN/TELEGRAM_CHAT_ID/MongoDb_ConnectionString/MongoDb_DatabaseName are process-wide
/// environment variables that ConfigurationExtensions.GetArbitrageConfig() (and ConfigService's copy
/// of the same logic) read on every call. Tests that set/clear them must run sequentially relative to
/// each other, or a concurrently-running test can observe another test's temporary override.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedEnvironmentVariablesCollection
{
    public const string Name = "SharedEnvironmentVariables";

    private SharedEnvironmentVariablesCollection() { }
}
