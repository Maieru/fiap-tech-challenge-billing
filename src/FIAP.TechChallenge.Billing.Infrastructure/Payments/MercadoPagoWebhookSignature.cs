using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace FIAP.TechChallenge.Billing.Infrastructure.Payments;

public sealed class MercadoPagoWebhookSignature(IConfiguration configuration)
{
    public bool IsValid(string signature, string requestId, string dataId)
    {
        var secret = configuration["MercadoPago:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(requestId)
            || string.IsNullOrWhiteSpace(dataId) || signature.Length > 512) return false;
        string? timestamp = null, hash = null;
        foreach (var part in signature.Split(','))
        {
            var pair = part.Trim().Split('=', 2);
            if (pair.Length != 2) return false;
            if (pair[0] == "ts") { if (timestamp is not null) return false; timestamp = pair[1]; }
            if (pair[0] == "v1") { if (hash is not null) return false; hash = pair[1]; }
        }
        if (timestamp is null || !long.TryParse(timestamp, out _) || hash?.Length != 64) return false;
        var manifest = $"id:{dataId.ToLowerInvariant()};request-id:{requestId};ts:{timestamp};";
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(manifest));
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(hash)); }
        catch (FormatException) { return false; }
    }
}
