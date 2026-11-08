using FIAP.TechChallenge.Billing.Domain.Enums;

namespace FIAP.TechChallenge.Billing.Domain.Entities;

public sealed class Cobranca
{
    public Guid Id { get; private set; }
    public Guid OrdemServicoId { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid OrcamentoId { get; private set; }
    public decimal Valor { get; private set; }
    public string Moeda { get; private set; } = "BRL";
    public SituacaoCobranca Situacao { get; private set; }
    public string? GatewayPaymentId { get; private set; }
    public string? GatewayOrderId { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public Guid? PaymentRequestMessageId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? TraceParent { get; private set; }
    public string? TraceState { get; private set; }
    public string? Motivo { get; private set; }
    public DateTimeOffset CriadaEmUtc { get; private set; }
    public DateTimeOffset? ConcluidaEmUtc { get; private set; }
    private Cobranca() { }
    public static Cobranca Criar(Guid ordem, Guid cliente, Guid orcamento, decimal valor, string moeda, DateTimeOffset now)
    {
        if (ordem == Guid.Empty || cliente == Guid.Empty || orcamento == Guid.Empty || valor < 0 || moeda != "BRL")
            throw new ArgumentException("Dados da cobrança inválidos.");
        return new() { Id = Guid.NewGuid(), OrdemServicoId = ordem, ClienteId = cliente, OrcamentoId = orcamento, Valor = valor, Moeda = moeda, CriadaEmUtc = now };
    }
    public void RegistrarSolicitacao(Guid messageId, string correlationId, string? traceParent, string? traceState)
    {
        PaymentRequestMessageId = messageId;
        CorrelationId = correlationId;
        TraceParent = traceParent;
        TraceState = traceState;
    }
    public void IniciarCheckout(string orderId, string checkoutUrl)
    {
        if (Situacao != SituacaoCobranca.Pendente || GatewayOrderId is not null || string.IsNullOrWhiteSpace(orderId)
            || !Uri.TryCreate(checkoutUrl, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Checkout inválido.");
        GatewayOrderId = orderId;
        CheckoutUrl = checkoutUrl;
    }
    public void Concluir(bool aprovada, string? externalId, string? motivo, DateTimeOffset now)
    {
        if (Situacao != SituacaoCobranca.Pendente)
            throw new InvalidOperationException("Cobrança já concluída.");
        if ((aprovada && string.IsNullOrWhiteSpace(externalId)) || (!aprovada && string.IsNullOrWhiteSpace(motivo)))
            throw new ArgumentException("Resultado do gateway inválido.");
        Situacao = aprovada ? SituacaoCobranca.Confirmada : SituacaoCobranca.Recusada;
        GatewayPaymentId = externalId;
        Motivo = motivo;
        ConcluidaEmUtc = now;
    }
}
