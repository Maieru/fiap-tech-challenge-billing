using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Interfaces;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobrancaPorOrdem;

public sealed class RecuperarCobrancaPorOrdemUseCase(ICobrancaQueryRepository repository) : IRecuperarCobrancaPorOrdemUseCase
{
    public Task<Cobranca?> ExecuteAsync(RecuperarCobrancaPorOrdemCommand command, CancellationToken cancellationToken = default) => repository.GetByOrdemAsync(command.OrdemServicoId, cancellationToken);
}
