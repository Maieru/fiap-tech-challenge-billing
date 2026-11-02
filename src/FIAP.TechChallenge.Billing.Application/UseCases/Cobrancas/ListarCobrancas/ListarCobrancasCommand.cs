using FIAP.TechChallenge.Billing.Domain.Enums;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ListarCobrancas;

public sealed record ListarCobrancasCommand(Guid? OrdemServicoId = null, SituacaoCobranca? Situacao = null, int PageNumber = 1, int PageSize = 10);
