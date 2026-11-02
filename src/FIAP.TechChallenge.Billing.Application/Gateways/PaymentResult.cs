namespace FIAP.TechChallenge.Billing.Application.Gateways;

public sealed record PaymentResult(bool Aprovada, string? GatewayPaymentId, string? Motivo);
