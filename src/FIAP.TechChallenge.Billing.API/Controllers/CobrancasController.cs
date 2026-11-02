using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ListarCobrancas;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobranca;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobrancaPorOrdem;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FIAP.TechChallenge.Billing.API.Controllers;

[ApiController, Authorize, Route("api/billing/v1/cobrancas")]
public sealed class CobrancasController : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] ListarCobrancasCommand command, [FromServices] IListarCobrancasUseCase useCase, CancellationToken cancellationToken)
    {
        try { return Ok(await useCase.ExecuteAsync(command, cancellationToken)); }
        catch (ArgumentException) { return BadRequest(new { error = "Paginação ou situação inválida.", errorCode = "Validation" }); }
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, [FromServices] IRecuperarCobrancaUseCase useCase, CancellationToken cancellationToken) =>
        await useCase.ExecuteAsync(new(id), cancellationToken) is { } charge ? Ok(charge) : NotFound(new { error = "Cobrança não encontrada.", errorCode = "NotFound" });
    [HttpGet("/api/billing/v1/ordens/{ordemServicoId:guid}/cobranca")]
    public async Task<IActionResult> GetByOrdem(Guid ordemServicoId, [FromServices] IRecuperarCobrancaPorOrdemUseCase useCase, CancellationToken cancellationToken) =>
        await useCase.ExecuteAsync(new(ordemServicoId), cancellationToken) is { } charge ? Ok(charge) : NotFound(new { error = "Cobrança não encontrada.", errorCode = "NotFound" });
}
