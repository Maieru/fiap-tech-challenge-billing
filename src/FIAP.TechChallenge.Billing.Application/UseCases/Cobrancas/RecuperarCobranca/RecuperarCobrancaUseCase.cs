using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Interfaces;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobranca;

public sealed class RecuperarCobrancaUseCase(ICobrancaQueryRepository repository) : IRecuperarCobrancaUseCase
{
    public Task<Cobranca?> ExecuteAsync(RecuperarCobrancaCommand command, CancellationToken cancellationToken = default) => repository.GetByIdAsync(command.Id, cancellationToken);
}
