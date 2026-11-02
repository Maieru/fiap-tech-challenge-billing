namespace FIAP.TechChallenge.Billing.Application.Gateways;

public sealed record PaymentRequest(Guid CobrancaId, Guid OrdemServicoId, decimal Valor, string Moeda, string IdempotencyKey);
