using System.Net;
using System.Text;
using System.Text.Json;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;

namespace FIAP.TechChallenge.Billing.Tests.Infrastructure.Payments;

public class MercadoPagoPaymentGatewayTests
{
    private static IConfiguration Configuration(string? token = "test-token") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["MercadoPago:AccessToken"] = token }).Build();

    [Test]
    public async Task CreatesHostedCheckoutPreservingAmountAndIdempotencyAcrossRedelivery()
    {
        var order = Guid.NewGuid();
        using var http = new HttpClient(new Handler(async request =>
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Multiple(() =>
            {
                Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/v1/orders"));
                Assert.That(request.Headers.Authorization!.ToString(), Is.EqualTo("Bearer test-token"));
                Assert.That(request.Headers.GetValues("X-Idempotency-Key").Single(), Is.EqualTo(order.ToString("D")));
                Assert.That(payload.RootElement.GetProperty("total_amount").GetString(), Is.EqualTo("12.345"));
                Assert.That(payload.RootElement.GetProperty("external_reference").GetString(), Is.EqualTo(order.ToString("D")));
                Assert.That(payload.RootElement.GetProperty("capture_mode").GetString(), Is.EqualTo("automatic"));
            });
            return Json(new { id = "ORD123", checkout_url = "https://www.mercadopago.com.br/checkout/test" });
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var gateway = new MercadoPagoPaymentGateway(http, Configuration());
        var request = new PaymentRequest(Guid.NewGuid(), order, 12.345m, "BRL", order.ToString("D"));
        var first = await gateway.CreateCheckoutAsync(request, default);
        var second = await gateway.CreateCheckoutAsync(request with { CobrancaId = Guid.NewGuid() }, default);
        Assert.That(first, Is.EqualTo(second));
    }

    [TestCase("processed", "accredited", PaymentStatus.Approved)]
    [TestCase("failed", "rejected_by_issuer", PaymentStatus.Rejected)]
    [TestCase("canceled", "canceled", PaymentStatus.Rejected)]
    [TestCase("created", "created", PaymentStatus.Pending)]
    [TestCase("processing", "in_process", PaymentStatus.Pending)]
    [TestCase("action_required", "waiting_capture", PaymentStatus.Pending)]
    [TestCase("processed", "partially_refunded", PaymentStatus.Pending)]
    [TestCase("unknown", "unknown", PaymentStatus.Pending)]
    public async Task QueriesAuthoritativeOrderStatus(string status, string detail, PaymentStatus expected)
    {
        var order = Guid.NewGuid();
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.That(request.RequestUri!.AbsolutePath, Is.EqualTo("/v1/orders/ORD123"));
            return Task.FromResult(Json(new { id = "ORD123", external_reference = order.ToString(), total_amount = "12.345", currency = "BRL", status, status_detail = detail }));
        })) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var result = await new MercadoPagoPaymentGateway(http, Configuration()).GetOrderAsync("ORD123", default);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(expected));
            Assert.That(result.OrdemServicoId, Is.EqualTo(order));
            Assert.That(result.Valor, Is.EqualTo(12.345m));
            Assert.That(result.Motivo, Is.EqualTo(expected == PaymentStatus.Rejected ? detail : null));
        });
    }

    [TestCase(401)]
    [TestCase(429)]
    [TestCase(500)]
    public void HttpFailuresAreTechnicalErrors(int status)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)))) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        Assert.ThrowsAsync<HttpRequestException>(() => new MercadoPagoPaymentGateway(http, Configuration()).GetOrderAsync("ORD123", default));
    }

    [Test]
    public void MissingCredentialDoesNotMakeRequest()
    {
        using var http = new HttpClient(new Handler(_ => throw new AssertionException("Unexpected HTTP request")));
        Assert.ThrowsAsync<InvalidOperationException>(() => new MercadoPagoPaymentGateway(http, Configuration(null)).GetOrderAsync("ORD123", default));
    }

    [TestCase("http://www.mercadopago.com.br/checkout")]
    [TestCase("https://attacker.invalid/checkout")]
    public void RejectsInvalidCheckoutUrl(string url)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(new { id = "ORD123", checkout_url = url })))) { BaseAddress = new Uri("https://api.mercadopago.com/") };
        var order = Guid.NewGuid();
        Assert.ThrowsAsync<InvalidOperationException>(() => new MercadoPagoPaymentGateway(http, Configuration()).CreateCheckoutAsync(new(Guid.NewGuid(), order, 10, "BRL", order.ToString()), default));
    }

    private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }
}
