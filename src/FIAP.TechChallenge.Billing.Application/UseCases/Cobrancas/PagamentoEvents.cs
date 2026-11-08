using FIAP.TechChallenge.Billing.Application.Messaging;
using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Enums;
using Maieru.FiapTechChallenge.Chassis;

namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas;

internal static class PagamentoEvents
{
    public static Task PublishAsync(Cobranca charge, IMessagingService messaging, CancellationToken ct)
    {
        MessageEnvelope<T> Envelope<T>(string type, T payload) => new(charge.Id, type, 1,
            charge.ConcluidaEmUtc!.Value, charge.CorrelationId!, charge.PaymentRequestMessageId,
            charge.TraceParent, charge.TraceState, payload);
        return charge.Situacao == SituacaoCobranca.Confirmada
            ? messaging.PublishAsync(BillingMessaging.Topic, charge.OrdemServicoId.ToString("D"),
                Envelope(PagamentoConfirmado.Type, new PagamentoConfirmado(charge.OrdemServicoId, charge.OrcamentoId,
                    charge.Id, charge.Valor, charge.Moeda, charge.GatewayPaymentId!)), ct)
            : messaging.PublishAsync(BillingMessaging.Topic, charge.OrdemServicoId.ToString("D"),
                Envelope(PagamentoRecusado.Type, new PagamentoRecusado(charge.OrdemServicoId, charge.OrcamentoId,
                    charge.Id, charge.Valor, charge.Moeda, charge.Motivo!)), ct);
    }
}
