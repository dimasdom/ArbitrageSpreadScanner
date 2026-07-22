using ArbitrageScanner.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ArbitrageScanner.Infrastructure.HealthChecks;

public class MongoHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;

    public MongoHealthCheck(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = _configuration.GetArbitrageConfig().MongoDb.ConnectionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return HealthCheckResult.Unhealthy("MongoDb connection string is not configured (Arbitrage:MongoDb:ConnectionString or MongoDb_ConnectionString)");
            }

            var client = new MongoClient(connectionString);
            await client.GetDatabase("admin").RunCommandAsync((Command<BsonDocument>)"{ ping: 1 }", cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
