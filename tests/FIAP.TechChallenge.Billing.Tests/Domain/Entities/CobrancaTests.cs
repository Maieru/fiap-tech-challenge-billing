using FIAP.TechChallenge.Billing.Domain.Entities;
using FIAP.TechChallenge.Billing.Domain.Enums;

namespace FIAP.TechChallenge.Billing.Tests.Domain.Entities;

public class CobrancaTests
{
    [Test]
    public void RejectInvalidAndRepeatedOutcomes()
    {
        var c = Cobranca.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, "BRL", DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => c.Concluir(true, null, null, DateTimeOffset.UtcNow));
        c.Concluir(false, null, "recusado", DateTimeOffset.UtcNow);
        Assert.Multiple(() => { Assert.That(c.Situacao, Is.EqualTo(SituacaoCobranca.Recusada)); Assert.Throws<InvalidOperationException>(() => c.Concluir(true, "x", null, DateTimeOffset.UtcNow)); Assert.Throws<ArgumentException>(() => Cobranca.Criar(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), -1, "USD", DateTimeOffset.UtcNow)); });
    }
}
