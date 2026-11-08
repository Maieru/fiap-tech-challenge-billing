using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.AtualizarPagamento;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FIAP.TechChallenge.Billing.API.Controllers;

[ApiController, AllowAnonymous, Route("api/billing/v1/webhooks/mercadopago")]
public sealed class MercadoPagoWebhookController(MercadoPagoWebhookSignature signature, IAtualizarPagamentoUseCase useCase,
    ILogger<MercadoPagoWebhookController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var id = Request.Query["data.id"].ToString();
        if (!signature.IsValid(Request.Headers["x-signature"].ToString(), Request.Headers["x-request-id"].ToString(), id))
            return Unauthorized();
        if (Request.Query["type"] != "order") return Ok();
        try
        {
            await useCase.ExecuteAsync(id, ct);
            return Ok();
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            // Do not log provider payloads, headers or secrets.
            logger.LogWarning("Falha ao processar notificação Mercado Pago: {ErrorType}", error.GetType().Name);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
