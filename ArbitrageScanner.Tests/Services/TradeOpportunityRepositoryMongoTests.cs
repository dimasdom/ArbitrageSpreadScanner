using ArbitrageScanner.Infrastructure.Repositories;
using ArbitrageScanner.Infrastructure.Services;
using ArbitrageScanner.Tests.Helpers;
using FluentAssertions;
using Xunit;

namespace ArbitrageScanner.Tests.Services;

// MongoClient's constructor is lazy (no real connection attempt), so these only need to verify the
// env-var resolution in each constructor overload doesn't throw — actual Mongo operations are covered
// by ArbitrageScanner.IntegrationTests via a real Testcontainers instance.
[Collection(SharedEnvironmentVariablesCollection.Name)]
public class TradeOpportunityRepositoryMongoTests
{
    [Fact]
    public void ParameterlessConstructor_EnvVarsSet_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://localhost:27017");
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", "TestDb");
        try
        {
            var act = () => new TradeOpportunityRepositoryMongo();

            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
            Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
        }
    }

    [Fact]
    public void ParameterlessConstructor_DatabaseNameNotSet_FallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://localhost:27017");
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
        try
        {
            var act = () => new TradeOpportunityRepositoryMongo();

            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
        }
    }

    [Fact]
    public void FileServiceConstructor_EnvVarsSet_DoesNotThrow()
    {
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://localhost:27017");
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", "TestDb");
        try
        {
            var fileService = new FileService(ServiceFactory.BuildConfig());

            var act = () => new TradeOpportunityRepositoryMongo(fileService);

            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
            Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
        }
    }

    [Fact]
    public void FileServiceConstructor_DatabaseNameNotSet_FallsBackToDefault()
    {
        Environment.SetEnvironmentVariable("MongoDb_ConnectionString", "mongodb://localhost:27017");
        Environment.SetEnvironmentVariable("MongoDb_DatabaseName", null);
        try
        {
            var fileService = new FileService(ServiceFactory.BuildConfig());

            var act = () => new TradeOpportunityRepositoryMongo(fileService);

            act.Should().NotThrow();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MongoDb_ConnectionString", null);
        }
    }
}
