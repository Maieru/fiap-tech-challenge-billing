using FIAP.TechChallenge.Billing.Infrastructure.Persistence.Entities;
using FIAP.TechChallenge.Billing.Application.Abstractions;
using FIAP.TechChallenge.Billing.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace FIAP.TechChallenge.Billing.Infrastructure.Persistence;


public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options), IBillingStore
{
    public DbSet<Cobranca> Cobrancas => Set<Cobranca>();
    public DbSet<ProcessedMessage> MensagensProcessadas => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Cobranca>().HasIndex(x => x.OrdemServicoId).IsUnique();
        model.Entity<Cobranca>().Property(x => x.Valor).HasColumnType("numeric");
        model.Entity<Cobranca>().Property(x => x.Moeda).HasMaxLength(3);
    }
    public async Task<IAsyncDisposable> BeginAsync(CancellationToken ct) => await Database.BeginTransactionAsync(ct);
    public Task<bool> ProcessedAsync(Guid id, CancellationToken ct) => MensagensProcessadas.AnyAsync(x => x.Id == id, ct);
    public async Task<Cobranca?> FindOrderAsync(Guid id, CancellationToken ct)
    {
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({id.ToString()}, 0))", ct);
        return await Cobrancas.SingleOrDefaultAsync(x => x.OrdemServicoId == id, ct);
    }
    public async Task SaveAsync(Cobranca cobranca, Guid messageId, CancellationToken ct)
    {
        if (Entry(cobranca).State == EntityState.Detached) Cobrancas.Add(cobranca);
        MensagensProcessadas.Add(new() { Id = messageId }); await SaveChangesAsync(ct);
    }
    public Task CommitAsync(CancellationToken ct) => Database.CurrentTransaction!.CommitAsync(ct);
}
