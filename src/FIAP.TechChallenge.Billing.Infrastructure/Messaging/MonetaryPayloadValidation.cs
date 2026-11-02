using System.Text.Json;

namespace FIAP.TechChallenge.Billing.Infrastructure.Messaging;

public static class MonetaryPayloadValidation
{
    public static void RequireDecimal(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out _))
            throw new JsonException($"O campo monetário '{propertyName}' é obrigatório e deve ser um número decimal válido.");
    }
}
