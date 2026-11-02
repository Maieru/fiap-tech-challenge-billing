using FIAP.TechChallenge.Billing.Application.Gateways;
namespace FIAP.TechChallenge.Billing.Infrastructure.Payments;

public sealed class MockPaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new PaymentResult(true, "mock-" + request.IdempotencyKey, null));
    }
}
