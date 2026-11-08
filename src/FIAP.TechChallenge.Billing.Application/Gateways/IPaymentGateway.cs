namespace FIAP.TechChallenge.Billing.Application.Gateways;

public interface IPaymentGateway
{
    Task<CheckoutResult> CreateCheckoutAsync(PaymentRequest request, CancellationToken cancellationToken);
    Task<PaymentOrder> GetOrderAsync(string orderId, CancellationToken cancellationToken);
}
