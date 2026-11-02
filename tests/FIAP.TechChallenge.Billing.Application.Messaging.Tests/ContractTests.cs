using System.Text.Json;
using FIAP.TechChallenge.Billing.Application.Messaging;
using NUnit.Framework;
namespace FIAP.TechChallenge.Billing.Application.Messaging.Tests;

public class ContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public void ReadsExistingV1ConfirmedPayment()
    {
        const string json = """{"ordemServicoId":"11111111-1111-1111-1111-111111111111","orcamentoId":"22222222-2222-2222-2222-222222222222","cobrancaId":"33333333-3333-3333-3333-333333333333","valor":12.345,"moeda":"BRL","gatewayPaymentId":"mock-id"}""";
        var payment = JsonSerializer.Deserialize<PagamentoConfirmado>(json, JsonOptions)!;
        Assert.Multiple(() => { Assert.That(payment.Valor, Is.EqualTo(12.345m)); Assert.That(payment.GatewayPaymentId, Is.EqualTo("mock-id")); Assert.That(PagamentoConfirmado.Type, Is.EqualTo("billing.pagamento-confirmado")); Assert.That(BillingMessaging.Version, Is.EqualTo(1)); });
    }
    [Test]
    public void RejectionWireUsesCamelCase()
    {
        var json = JsonSerializer.Serialize(new PagamentoRecusado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, "BRL", "recusado"), JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Multiple(() => { Assert.That(document.RootElement.GetProperty("motivo").GetString(), Is.EqualTo("recusado")); Assert.That(document.RootElement.GetProperty("valor").GetDecimal(), Is.Zero); });
    }
}
