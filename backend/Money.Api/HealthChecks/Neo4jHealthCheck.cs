using Microsoft.Extensions.Diagnostics.HealthChecks;
using Neo4j.Driver;

namespace Money.Api.HealthChecks;

public class Neo4jHealthCheck(IDriver driver) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
    {
        try
        {
            await driver.VerifyConnectivityAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Neo4j unavailable", ex);
        }
    }
}
