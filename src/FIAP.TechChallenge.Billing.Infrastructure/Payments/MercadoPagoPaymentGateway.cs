using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FIAP.TechChallenge.Billing.Application.Gateways;
using Microsoft.Extensions.Configuration;

namespace FIAP.TechChallenge.Billing.Infrastructure.Payments;

public sealed class MercadoPagoPaymentGateway(HttpClient http, IConfiguration configuration) : IPaymentGateway
{
    public async Task<CheckoutResult> CreateCheckoutAsync(PaymentRequest request, CancellationToken ct)
    {
        using var message = Request(HttpMethod.Post, "v1/orders");
        message.Headers.Add("X-Idempotency-Key", request.IdempotencyKey);
        message.Content = JsonContent.Create(new
        {
            type = "online", processing_mode = "manual", capture_mode = "automatic",
            total_amount = request.Valor.ToString(CultureInfo.InvariantCulture),
            external_reference = request.OrdemServicoId.ToString("D"),
            items = new[] { new { title = "Ordem de serviço " + request.OrdemServicoId.ToString("D"),
                quantity = 1, unit_price = request.Valor.ToString(CultureInfo.InvariantCulture) } }
        });
        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;
        var id = root.GetProperty("id").GetString();
        var url = root.GetProperty("checkout_url").GetString();
        if (string.IsNullOrWhiteSpace(id) || !Uri.TryCreate(url, UriKind.Absolute, out var checkout)
            || checkout.Scheme != Uri.UriSchemeHttps || checkout.Host != "www.mercadopago.com.br")
            throw new InvalidOperationException("Resposta de checkout inválida do Mercado Pago.");
        return new(id, url!);
    }

    public async Task<PaymentOrder> GetOrderAsync(string orderId, CancellationToken ct)
    {
        using var message = Request(HttpMethod.Get, "v1/orders/" + Uri.EscapeDataString(orderId));
        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = document.RootElement;
        var status = root.GetProperty("status").GetString();
        // Newly created orders can omit status_detail until a payment is attempted.
        var detail = root.TryGetProperty("status_detail", out var statusDetail) ? statusDetail.GetString() : null;
        var state = (status, detail) switch
        {
            ("processed", "accredited") => PaymentStatus.Approved,
            ("failed" or "canceled", _) => PaymentStatus.Rejected,
            _ => PaymentStatus.Pending
        };
        var id = root.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(root.GetProperty("external_reference").GetString(), out var order))
            throw new InvalidOperationException("Referência inválida do Mercado Pago.");
        var amount = root.GetProperty("total_amount");
        return new(id, order, amount.ValueKind == JsonValueKind.String
            ? decimal.Parse(amount.GetString()!, CultureInfo.InvariantCulture) : amount.GetDecimal(),
            root.GetProperty("currency").GetString()!, state,
            state == PaymentStatus.Rejected ? detail ?? status : null);
    }

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var token = configuration["MercadoPago:AccessToken"];
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Configure MercadoPago:AccessToken.");
        var message = new HttpRequestMessage(method, path);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return message;
    }
}
