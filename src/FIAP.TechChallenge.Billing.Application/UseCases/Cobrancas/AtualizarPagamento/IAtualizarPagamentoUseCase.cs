namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.AtualizarPagamento;

public interface IAtualizarPagamentoUseCase
{
    Task ExecuteAsync(string gatewayOrderId, CancellationToken ct = default);
}
