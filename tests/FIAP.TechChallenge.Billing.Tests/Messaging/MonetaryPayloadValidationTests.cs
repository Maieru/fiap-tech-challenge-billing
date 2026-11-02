using System.Text.Json;
using FIAP.TechChallenge.Billing.Infrastructure.Messaging;

namespace FIAP.TechChallenge.Billing.Tests.Messaging;

public class MonetaryPayloadValidationTests
{
    [TestCase("{}")]
    [TestCase("{\"valor\":null}")]
    [TestCase("{\"valor\":\"0\"}")]
    [TestCase("{\"valor\":1e100}")]
    [TestCase("[]")]
    public void RejectsMissingNullStringOverflowOrNonObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<JsonException>(() => MonetaryPayloadValidation.RequireDecimal(document.RootElement, "valor"));
    }
    [TestCase("{\"valor\":0}")]
    [TestCase("{\"valor\":12.345}")]
    [TestCase("{\"valor\":79228162514264337593543950335}")]
    public void AcceptsExplicitDecimalIncludingZero(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.DoesNotThrow(() => MonetaryPayloadValidation.RequireDecimal(document.RootElement, "valor"));
    }
}
