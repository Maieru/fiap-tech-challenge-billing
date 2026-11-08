using FIAP.TechChallenge.Billing.Application.Abstractions;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Application.Messaging;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.AtualizarPagamento;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;
using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Enums;
using Maieru.FiapTechChallenge.Chassis;
using Moq;

namespace FIAP.TechChallenge.Billing.Tests.Application;

public class PaymentWorkflowTests
{
    private readonly Mock<IBillingStore> store = new();
    private readonly Mock<IPaymentGateway> gateway = new();
    private readonly Mock<IMessagingService> messaging = new();

    [SetUp]
    public void Setup()
    {
        store.Reset(); gateway.Reset(); messaging.Reset();
        store.Setup(x => x.BeginAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new Transaction());
    }

    [TestCase(0)]
    [TestCase(12.345)]
    public async Task StartsCheckoutOrConfirmsZeroWithoutExternalCall(decimal amount)
    {
        var command = new ProcessarPagamentoCommand(Guid.NewGuid(), "correlation", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), amount, "BRL");
        gateway.Setup(x => x.CreateCheckoutAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckoutResult("ORD123", "https://www.mercadopago.com.br/checkout/test"));
        Cobranca? saved = null;
        store.Setup(x => x.SaveAsync(It.IsAny<Cobranca>(), command.MessageId, It.IsAny<CancellationToken>()))
            .Callback<Cobranca, Guid?, CancellationToken>((charge, _, _) => saved = charge).Returns(Task.CompletedTask);
        await new ProcessarPagamentoUseCase(store.Object, gateway.Object, messaging.Object, TimeProvider.System).ExecuteAsync(command);
        Assert.Multiple(() =>
        {
            Assert.That(saved!.Valor, Is.EqualTo(amount));
            Assert.That(saved.PaymentRequestMessageId, Is.EqualTo(command.MessageId));
            Assert.That(saved.CorrelationId, Is.EqualTo(command.CorrelationId));
            Assert.That(saved.Situacao, Is.EqualTo(amount == 0 ? SituacaoCobranca.Confirmada : SituacaoCobranca.Pendente));
        });
        gateway.Verify(x => x.CreateCheckoutAsync(It.Is<PaymentRequest>(r => r.IdempotencyKey == command.OrdemServicoId.ToString("D") && r.Valor == amount), It.IsAny<CancellationToken>()), amount == 0 ? Times.Never() : Times.Once());
        if (amount == 0)
        {
            Assert.That(saved!.GatewayPaymentId, Is.EqualTo("sem-cobranca-" + command.OrdemServicoId.ToString("D")));
            messaging.Verify(x => x.PublishAsync(BillingMessaging.Topic, command.OrdemServicoId.ToString("D"),
                It.Is<MessageEnvelope<PagamentoConfirmado>>(e => e.MessageId == saved.Id && e.CausationId == command.MessageId), It.IsAny<CancellationToken>()), Times.Once());
        }
        else messaging.VerifyNoOtherCalls();
        store.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once());
    }

    [TestCase(PaymentStatus.Pending)]
    [TestCase(PaymentStatus.Approved)]
    [TestCase(PaymentStatus.Rejected)]
    public async Task WebhookPublishesOnlyTerminalOutcomeAndIgnoresRepeatsAndLatePending(PaymentStatus status)
    {
        var charge = PendingCharge();
        var result = new PaymentOrder("ORD123", charge.OrdemServicoId, charge.Valor, "BRL", status, status == PaymentStatus.Rejected ? "canceled" : null);
        store.Setup(x => x.FindOrderAsync(charge.OrdemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(charge);
        gateway.Setup(x => x.GetOrderAsync("ORD123", It.IsAny<CancellationToken>())).ReturnsAsync(result);
        var useCase = new AtualizarPagamentoUseCase(store.Object, gateway.Object, messaging.Object, TimeProvider.System);
        await useCase.ExecuteAsync("ORD123");
        await useCase.ExecuteAsync("ORD123");
        gateway.Setup(x => x.GetOrderAsync("ORD123", It.IsAny<CancellationToken>())).ReturnsAsync(result with { Status = PaymentStatus.Pending });
        await useCase.ExecuteAsync("ORD123");
        Assert.That(charge.Situacao, Is.EqualTo(status switch { PaymentStatus.Approved => SituacaoCobranca.Confirmada, PaymentStatus.Rejected => SituacaoCobranca.Recusada, _ => SituacaoCobranca.Pendente }));
        if (status == PaymentStatus.Pending) messaging.VerifyNoOtherCalls();
        else
        {
            Assert.That(messaging.Invocations.Count, Is.EqualTo(1));
            if (status == PaymentStatus.Approved)
                messaging.Verify(x => x.PublishAsync(BillingMessaging.Topic, charge.OrdemServicoId.ToString("D"),
                    It.Is<MessageEnvelope<PagamentoConfirmado>>(e => e.MessageId == charge.Id && e.CausationId == charge.PaymentRequestMessageId && e.CorrelationId == charge.CorrelationId), It.IsAny<CancellationToken>()), Times.Once());
            else
                messaging.Verify(x => x.PublishAsync(BillingMessaging.Topic, charge.OrdemServicoId.ToString("D"),
                    It.Is<MessageEnvelope<PagamentoRecusado>>(e => e.MessageId == charge.Id && e.CausationId == charge.PaymentRequestMessageId && e.CorrelationId == charge.CorrelationId), It.IsAny<CancellationToken>()), Times.Once());
            store.Verify(x => x.SaveAsync(charge, null, It.IsAny<CancellationToken>()), Times.Once());
            store.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once());
        }
    }

    [TestCase("id")]
    [TestCase("reference")]
    [TestCase("value")]
    [TestCase("currency")]
    [TestCase("missing")]
    public void DivergentOrMissingChargeDoesNotConclude(string mismatch)
    {
        var charge = PendingCharge();
        var order = new PaymentOrder(mismatch == "id" ? "ORD456" : "ORD123", mismatch == "reference" ? Guid.NewGuid() : charge.OrdemServicoId,
            mismatch == "value" ? 99 : charge.Valor, mismatch == "currency" ? "ARS" : "BRL", PaymentStatus.Approved, null);
        gateway.Setup(x => x.GetOrderAsync("ORD123", It.IsAny<CancellationToken>())).ReturnsAsync(order);
        store.Setup(x => x.FindOrderAsync(charge.OrdemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(mismatch == "missing" ? null : charge);
        Assert.ThrowsAsync<InvalidOperationException>(() => new AtualizarPagamentoUseCase(store.Object, gateway.Object, messaging.Object, TimeProvider.System).ExecuteAsync("ORD123"));
        Assert.That(charge.Situacao, Is.EqualTo(SituacaoCobranca.Pendente));
        messaging.VerifyNoOtherCalls();
        store.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    [TestCase("gateway")]
    [TestCase("kafka")]
    public void TechnicalFailureDoesNotCommit(string failure)
    {
        var charge = PendingCharge();
        store.Setup(x => x.FindOrderAsync(charge.OrdemServicoId, It.IsAny<CancellationToken>())).ReturnsAsync(charge);
        if (failure == "gateway") gateway.Setup(x => x.GetOrderAsync("ORD123", It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException());
        else
        {
            gateway.Setup(x => x.GetOrderAsync("ORD123", It.IsAny<CancellationToken>())).ReturnsAsync(new PaymentOrder("ORD123", charge.OrdemServicoId, charge.Valor, "BRL", PaymentStatus.Approved, null));
            messaging.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MessageEnvelope<PagamentoConfirmado>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new IOException());
        }
        Assert.That(async () => await new AtualizarPagamentoUseCase(store.Object, gateway.Object, messaging.Object, TimeProvider.System).ExecuteAsync("ORD123"), Throws.Exception);
        store.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never());
    }

    private static Cobranca PendingCharge()
    {
        var charge = Cobranca.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12.345m, "BRL", DateTimeOffset.UtcNow);
        charge.RegistrarSolicitacao(Guid.NewGuid(), "correlation", null, null);
        charge.IniciarCheckout("ORD123", "https://www.mercadopago.com.br/checkout/test");
        return charge;
    }
    private sealed class Transaction : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
