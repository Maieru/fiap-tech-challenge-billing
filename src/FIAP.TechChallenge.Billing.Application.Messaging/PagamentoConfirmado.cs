namespace FIAP.TechChallenge.Billing.Application.Messaging;

public sealed record PagamentoConfirmado(Guid OrdemServicoId, Guid OrcamentoId, Guid CobrancaId, decimal Valor, string Moeda, string GatewayPaymentId)
{
    public const string Type = "billing.pagamento-confirmado";
}
