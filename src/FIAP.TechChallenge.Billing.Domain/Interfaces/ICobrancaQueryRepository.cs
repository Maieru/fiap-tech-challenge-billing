using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Enums;

namespace FIAP.TechChallenge.Billing.Domain.Interfaces;

public interface ICobrancaQueryRepository
{
    Task<Cobranca?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Cobranca?> GetByOrdemAsync(Guid ordemServicoId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Cobranca> Items, int TotalItems)> GetPagedAsync(Guid? ordemServicoId, SituacaoCobranca? situacao, int pageNumber, int pageSize, CancellationToken cancellationToken);
}
