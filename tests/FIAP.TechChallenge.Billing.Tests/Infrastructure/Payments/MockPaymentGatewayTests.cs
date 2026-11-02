using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Enums;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;

namespace FIAP.TechChallenge.Billing.Tests.Infrastructure.Payments;

public class MockPaymentGatewayTests
{
    [TestCase(0)]
    [TestCase(12.345)]
    public async Task MockPreservesAmountAndIsIdempotent(decimal value)
    {
        var c = Cobranca.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), value, "BRL", DateTimeOffset.UtcNow);
        var gateway = new MockPaymentGateway(); var request = new PaymentRequest(c.Id, c.OrdemServicoId, value, "BRL", c.OrdemServicoId.ToString("D"));
        var first = await gateway.ChargeAsync(request, default); var repeated = await gateway.ChargeAsync(request with { CobrancaId = Guid.NewGuid() }, default);
        c.Concluir(first.Aprovada, first.GatewayPaymentId, null, DateTimeOffset.UtcNow);
        Assert.Multiple(() => { Assert.That(first, Is.EqualTo(repeated)); Assert.That(c.Valor, Is.EqualTo(value)); Assert.That(c.Situacao, Is.EqualTo(SituacaoCobranca.Confirmada)); });
    }
}
