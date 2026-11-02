using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Enums;
using FIAP.TechChallenge.Billing.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FIAP.TechChallenge.Billing.Infrastructure.Persistence.Repositories;

public sealed class CobrancaQueryRepository(BillingDbContext db) : ICobrancaQueryRepository
{
    public Task<Cobranca?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => db.Cobrancas.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<Cobranca?> GetByOrdemAsync(Guid ordemServicoId, CancellationToken cancellationToken) => db.Cobrancas.AsNoTracking().SingleOrDefaultAsync(x => x.OrdemServicoId == ordemServicoId, cancellationToken);
    public async Task<(IReadOnlyList<Cobranca> Items, int TotalItems)> GetPagedAsync(Guid? ordemServicoId, SituacaoCobranca? situacao, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Cobrancas.AsNoTracking();
        if (ordemServicoId.HasValue) query = query.Where(x => x.OrdemServicoId == ordemServicoId);
        if (situacao.HasValue) query = query.Where(x => x.Situacao == situacao);
        var items = await query.OrderBy(x => x.CriadaEmUtc).ThenBy(x => x.Id).Skip((pageNumber - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return (items, await query.CountAsync(cancellationToken));
    }
}
