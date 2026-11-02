using FIAP.TechChallenge.Billing.Domain.Entities;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ListarCobrancas;

public sealed record ListarCobrancasResponse(IReadOnlyList<Cobranca> Items, int TotalItems, int PageNumber, int PageSize);
