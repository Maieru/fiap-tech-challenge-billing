using FIAP.TechChallenge.Billing.Domain.Entities;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobranca;

public interface IRecuperarCobrancaUseCase { Task<Cobranca?> ExecuteAsync(RecuperarCobrancaCommand command, CancellationToken cancellationToken = default); }
