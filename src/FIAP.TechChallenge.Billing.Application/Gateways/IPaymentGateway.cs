namespace FIAP.TechChallenge.Billing.Application.Gateways;

public interface IPaymentGateway { Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken); }
