namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;

public sealed record ProcessarPagamentoCommand(Guid MessageId, string CorrelationId, Guid OrdemServicoId, Guid ClienteId, Guid OrcamentoId, decimal Valor, string Moeda);
