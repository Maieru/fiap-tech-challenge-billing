using FIAP.TechChallenge.Billing.Application.Abstractions;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Domain.Enums;
using Maieru.FiapTechChallenge.Chassis;

namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.AtualizarPagamento;

public sealed class AtualizarPagamentoUseCase(IBillingStore store, IPaymentGateway gateway, IMessagingService messaging, TimeProvider clock) : IAtualizarPagamentoUseCase
{
    public async Task ExecuteAsync(string gatewayOrderId, CancellationToken ct = default)
    {
        var order = await gateway.GetOrderAsync(gatewayOrderId, ct);
        await using var transaction = await store.BeginAsync(ct);
        var charge = await store.FindOrderAsync(order.OrdemServicoId, ct)
            ?? throw new InvalidOperationException("Cobrança ainda não disponível.");
        if (order.Id != gatewayOrderId || charge.GatewayOrderId != order.Id || charge.Valor != order.Valor || charge.Moeda != order.Moeda)
            throw new InvalidOperationException("Pagamento divergente da cobrança.");
        if (charge.Situacao != SituacaoCobranca.Pendente || order.Status == PaymentStatus.Pending) return;
        charge.Concluir(order.Status == PaymentStatus.Approved, order.Id, order.Motivo, clock.GetUtcNow());
        await store.SaveAsync(charge, null, ct);
        await PagamentoEvents.PublishAsync(charge, messaging, ct);
        await store.CommitAsync(ct);
    }
}
