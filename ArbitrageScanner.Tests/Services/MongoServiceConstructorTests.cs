using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

[Collection(SharedEnvironmentVariablesCollection.Name)]
public class MongoServiceConstructorTests
{
    private static void ClearEnvVars()
    {
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
    }

    [Fact]
    public void Constructor_NoConnectionStringAndNoEnvVar_Throws()
    {
        ClearEnvVars();
        try
        {
            var act = () => new MongoService("", "SomeDb");
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*MongoDb_ConnectionString*");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void Constructor_NoDatabaseNameAndNoEnvVar_DefaultsWithoutThrowing()
    {
        ClearEnvVars();
        try
        {
            var act = () => new MongoService("mongodb://localhost:27017", "");
            act.Should().NotThrow();
        }
        finally
        {
            ClearEnvVars();
        }
    }
}
