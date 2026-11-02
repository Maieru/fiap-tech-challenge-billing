using FIAP.TechChallenge.Billing.Domain.Entities;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobrancaPorOrdem;

public interface IRecuperarCobrancaPorOrdemUseCase { Task<Cobranca?> ExecuteAsync(RecuperarCobrancaPorOrdemCommand command, CancellationToken cancellationToken = default); }
