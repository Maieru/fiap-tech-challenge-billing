using FIAP.TechChallenge.Billing.Infrastructure.Persistence;
using FIAP.TechChallenge.Billing.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Confluent.Kafka;

namespace FIAP.TechChallenge.Billing.API;

public sealed class BillingReadiness(BillingDbContext db, ConsumerState state, IConfiguration config) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!state.Running || !await db.Database.CanConnectAsync(cancellationToken)) return HealthCheckResult.Unhealthy("Banco ou consumidor indisponível.");
            using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = config["Kafka:BootstrapServers"] }).Build(); admin.GetMetadata(TimeSpan.FromSeconds(3)); return HealthCheckResult.Healthy();
        }
        catch (Exception e) { return HealthCheckResult.Unhealthy("Dependência indisponível.", e); }
    }
}
