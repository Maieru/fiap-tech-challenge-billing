using FIAP.TechChallenge.Billing.API.Controllers;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.AtualizarPagamento;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace FIAP.TechChallenge.Billing.Tests.Application;

public class WebhookControllerTests
{
    [TestCase(false, false, 401)]
    [TestCase(true, false, 200)]
    [TestCase(true, true, 503)]
    public async Task ReturnsRetryOnlyForProcessingFailure(bool signed, bool failure, int status)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MercadoPago:WebhookSecret"] = "secret" }).Build();
        var updater = new Mock<IAtualizarPagamentoUseCase>();
        if (failure) updater.Setup(x => x.ExecuteAsync("ORD123", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());
        var controller = new MercadoPagoWebhookController(new MercadoPagoWebhookSignature(config), updater.Object, NullLogger<MercadoPagoWebhookController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.Request.QueryString = new QueryString("?data.id=ORD123&type=order");
        if (signed)
        {
            controller.Request.Headers["x-request-id"] = "req";
            controller.Request.Headers["x-signature"] = "ts=1,v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("secret"), Encoding.UTF8.GetBytes("id:ord123;request-id:req;ts:1;")));
        }
        var result = await controller.Receive(default);
        var actual = result is ObjectResult objectResult ? objectResult.StatusCode : ((StatusCodeResult)result).StatusCode;
        Assert.That(actual, Is.EqualTo(status));
        updater.Verify(x => x.ExecuteAsync("ORD123", It.IsAny<CancellationToken>()), signed ? Times.Once() : Times.Never());
    }
}
