using FIAP.TechChallenge.Billing.Domain.Interfaces;
namespace FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ListarCobrancas;

public sealed class ListarCobrancasUseCase(ICobrancaQueryRepository repository) : IListarCobrancasUseCase
{
    public async Task<ListarCobrancasResponse> ExecuteAsync(ListarCobrancasCommand command, CancellationToken cancellationToken = default)
    {
        if (command.PageNumber < 1 || command.PageSize < 1 || command.PageSize > 100 || (long)(command.PageNumber - 1) * command.PageSize > int.MaxValue || (command.Situacao.HasValue && !Enum.IsDefined(command.Situacao.Value)))
            throw new ArgumentException("Paginação ou situação inválida.", nameof(command));
        var (items, totalItems) = await repository.GetPagedAsync(command.OrdemServicoId, command.Situacao, command.PageNumber, command.PageSize, cancellationToken);
        return new(items, totalItems, command.PageNumber, command.PageSize);
    }
}
