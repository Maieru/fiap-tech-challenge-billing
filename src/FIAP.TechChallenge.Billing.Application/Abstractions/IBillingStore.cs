using FIAP.TechChallenge.Billing.Domain.Entities;
namespace FIAP.TechChallenge.Billing.Application.Abstractions;

public interface IBillingStore
{
    Task<IAsyncDisposable> BeginAsync(CancellationToken ct);
    Task<bool> ProcessedAsync(Guid id, CancellationToken ct);
    Task<Cobranca?> FindOrderAsync(Guid id, CancellationToken ct);
    Task SaveAsync(Cobranca cobranca, Guid? messageId, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
