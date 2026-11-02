using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace FIAP.TechChallenge.Billing.Infrastructure.Persistence;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql("Host=localhost;Database=fiap_billing").Options);
}
