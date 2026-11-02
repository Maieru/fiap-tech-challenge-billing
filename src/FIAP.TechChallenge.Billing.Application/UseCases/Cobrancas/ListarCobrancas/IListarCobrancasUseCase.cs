namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ListarCobrancas;

public interface IListarCobrancasUseCase { Task<ListarCobrancasResponse> ExecuteAsync(ListarCobrancasCommand command, CancellationToken cancellationToken = default); }
