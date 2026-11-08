namespace FIAP.TechChallenge.Billing.Application.Gateways;

public enum PaymentStatus { Pending, Approved, Rejected }
public sealed record PaymentOrder(string Id, Guid OrdemServicoId, decimal Valor, string Moeda, PaymentStatus Status, string? Motivo);
