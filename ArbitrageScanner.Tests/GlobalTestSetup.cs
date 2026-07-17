using System.Runtime.CompilerServices;

namespace ArbitrageScanner.Tests;

internal static class GlobalTestSetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MongoDb_ConnectionString")))
            Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://localhost:27017");
    }
}
