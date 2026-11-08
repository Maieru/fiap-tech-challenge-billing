using System.Security.Cryptography;
using System.Text;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;

namespace FIAP.TechChallenge.Billing.Tests.Infrastructure.Payments;

public class MercadoPagoWebhookSignatureTests
{
    [Test]
    public void ValidatesLowercaseResourceAndRejectsTampering()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MercadoPago:WebhookSecret"] = "test-secret" }).Build();
        var validator = new MercadoPagoWebhookSignature(config);
        var signature = "ts=1742505638683,v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-secret"), Encoding.UTF8.GetBytes("id:ord123;request-id:req-1;ts:1742505638683;")));
        Assert.Multiple(() =>
        {
            Assert.That(validator.IsValid(signature, "req-1", "ORD123"), Is.True);
            Assert.That(validator.IsValid(signature, "req-2", "ORD123"), Is.False);
            Assert.That(validator.IsValid(signature, "req-1", "ORD456"), Is.False);
            Assert.That(validator.IsValid(signature, "", "ORD123"), Is.False);
            Assert.That(validator.IsValid(signature, "req-1", ""), Is.False);
            Assert.That(validator.IsValid(signature + ",ts=1", "req-1", "ORD123"), Is.False);
            Assert.That(validator.IsValid("ts=1,v1=" + new string('z', 64), "req-1", "ORD123"), Is.False);
            Assert.That(validator.IsValid("ts=invalid,v1=123", "req-1", "ORD123"), Is.False);
            Assert.That(new MercadoPagoWebhookSignature(new ConfigurationBuilder().Build()).IsValid(signature, "req-1", "ORD123"), Is.False);
        });
    }
}
