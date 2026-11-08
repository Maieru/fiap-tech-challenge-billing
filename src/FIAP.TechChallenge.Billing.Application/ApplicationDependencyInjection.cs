using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ListarCobrancas;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobranca;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.RecuperarCobrancaPorOrdem;
using Microsoft.Extensions.DependencyInjection;
namespace FIAP.TechChallenge.Billing.Application;

public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IProcessarPagamentoUseCase, ProcessarPagamentoUseCase>();
        services.AddScoped<UseCases.Cobrancas.AtualizarPagamento.IAtualizarPagamentoUseCase, UseCases.Cobrancas.AtualizarPagamento.AtualizarPagamentoUseCase>();
        services.AddScoped<IListarCobrancasUseCase, ListarCobrancasUseCase>();
        services.AddScoped<IRecuperarCobrancaUseCase, RecuperarCobrancaUseCase>();
        services.AddScoped<IRecuperarCobrancaPorOrdemUseCase, RecuperarCobrancaPorOrdemUseCase>();
        return services;
    }
}
