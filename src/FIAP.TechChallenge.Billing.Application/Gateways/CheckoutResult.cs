namespace FIAP.TechChallenge.Billing.Application.Gateways;

public sealed record CheckoutResult(string OrderId, string CheckoutUrl);
