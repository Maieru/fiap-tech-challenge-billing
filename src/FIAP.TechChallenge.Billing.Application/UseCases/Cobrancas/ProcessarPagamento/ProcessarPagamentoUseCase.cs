using FIAP.TechChallenge.Billing.Application.Abstractions;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Application.Messaging;
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
        var result = await gateway.ChargeAsync(new(charge.Id, ordem, valor, moeda, ordem.ToString("D")), ct);
        var now = clock.GetUtcNow();
        charge.Concluir(result.Aprovada, result.GatewayPaymentId, result.Motivo, now);
        await store.SaveAsync(charge, messageId, ct);
        MessageEnvelope<T> Envelope<T>(string type, T payload) => new(Guid.NewGuid(), type, 1, now, correlation, messageId, Activity.Current?.Id, Activity.Current?.TraceStateString, payload);

        if (result.Aprovada)
            await messaging.PublishAsync(BillingMessaging.Topic, ordem.ToString("D"), Envelope(PagamentoConfirmado.Type, new PagamentoConfirmado(ordem, orcamento, charge.Id, valor, moeda, result.GatewayPaymentId!)), ct);
        else
            await messaging.PublishAsync(BillingMessaging.Topic, ordem.ToString("D"), Envelope(PagamentoRecusado.Type, new PagamentoRecusado(ordem, orcamento, charge.Id, valor, moeda, result.Motivo!)), ct);

        await store.CommitAsync(ct);
    }
}
