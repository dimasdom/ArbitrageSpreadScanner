using ArbitrageScanner.Infrastructure.Common;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArbitrageScanner.Infrastructure.HealthChecks;

public class RabbitMqHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var factory = RabbitMqConnectionFactory.FromEnvironment();

            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(exception: ex);
        }
    }
}
