namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;

public interface IProcessarPagamentoUseCase
{
    Task ExecuteAsync(ProcessarPagamentoCommand command, CancellationToken cancellationToken = default);
}
