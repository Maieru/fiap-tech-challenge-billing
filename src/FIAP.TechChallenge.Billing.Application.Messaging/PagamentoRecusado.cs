namespace FIAP.TechChallenge.Billing.Application.Messaging;

public sealed record PagamentoRecusado(Guid OrdemServicoId, Guid OrcamentoId, Guid CobrancaId, decimal Valor, string Moeda, string Motivo)
{
    public const string Type = "billing.pagamento-recusado";
}
