using FIAP.TechChallenge.Billing.Application.Abstractions;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Domain.Entities;
using Maieru.FiapTechChallenge.Chassis;
using System.Diagnostics;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;

public sealed class ProcessarPagamentoUseCase(IBillingStore store, IPaymentGateway gateway, IMessagingService messaging, TimeProvider clock) : IProcessarPagamentoUseCase
{
    public async Task ExecuteAsync(ProcessarPagamentoCommand command, CancellationToken ct = default)
    {
        var (messageId, correlation, ordem, cliente, orcamento, valor, moeda) = command;
        await using var transaction = await store.BeginAsync(ct);

        if (await store.ProcessedAsync(messageId, ct))
            return;

        var existing = await store.FindOrderAsync(ordem, ct);

        if (await store.ProcessedAsync(messageId, ct))
            return;

        if (existing is not null)
        {
            if (existing.ClienteId != cliente || existing.OrcamentoId != orcamento || existing.Valor != valor || existing.Moeda != moeda)
                throw new InvalidOperationException("Solicitação conflitante.");
            await store.SaveAsync(existing, messageId, ct);
            await store.CommitAsync(ct);
            return;
        }

        var charge = Cobranca.Criar(ordem, cliente, orcamento, valor, moeda, clock.GetUtcNow());
        charge.RegistrarSolicitacao(messageId, correlation, Activity.Current?.Id, Activity.Current?.TraceStateString);
        if (valor == 0)
            charge.Concluir(true, "sem-cobranca-" + ordem.ToString("D"), null, clock.GetUtcNow());
        else
        {
            var checkout = await gateway.CreateCheckoutAsync(new(charge.Id, ordem, valor, moeda, ordem.ToString("D")), ct);
            charge.IniciarCheckout(checkout.OrderId, checkout.CheckoutUrl);
        }
        await store.SaveAsync(charge, messageId, ct);
        if (valor == 0) await PagamentoEvents.PublishAsync(charge, messaging, ct);

        await store.CommitAsync(ct);
    }
}
