using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
namespace FIAP.TechChallenge.Billing.Tests.Images;

public class BillingImageTests
{
    [TestCase("unsupported"), Category("ContainerImage")]
    [TestCase("missing-valor")]
    public async Task ImageSupportsJwtPersistenceOpenApiAndStopsOnInvalidMessage(string invalidKind)
    {
        var image = Environment.GetEnvironmentVariable("BILLING_TEST_IMAGE") ?? throw new InvalidOperationException("Configure BILLING_TEST_IMAGE.");
        await using var network = new NetworkBuilder().Build(); await network.CreateAsync();
        await using var db = new PostgreSqlBuilder("postgres:18.3").WithNetwork(network).WithNetworkAliases("postgres").Build();
        await using var kafka = new KafkaBuilder("confluentinc/cp-kafka:7.9.2").WithKRaft().WithNetwork(network).WithNetworkAliases("kafka").WithListener("kafka:19092").Build();
        await Task.WhenAll(db.StartAsync(), kafka.StartAsync());
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.GetBootstrapAddress() }).Build();
        await admin.CreateTopicsAsync(new[] { "oficina.os.billing.requests.v1", "oficina.billing.events.v1" }.Select(t => new TopicSpecification { Name = t, NumPartitions = 1, ReplicationFactor = 1 }));
        var connection = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = db.GetConnectionString() }; connection["Host"] = "postgres"; connection["Port"] = 5432;
        var key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); var group = "billing-image-" + Guid.NewGuid().ToString("N");
        await using var api = new ContainerBuilder(image).WithNetwork(network).WithPortBinding(8080, true)
         .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Testing").WithEnvironment("ASPNETCORE_HTTP_PORTS", "8080")
         .WithEnvironment("ConnectionStrings__DefaultConnection", connection.ToString()).WithEnvironment("Database__ApplyMigrations", "true")
         .WithEnvironment("Kafka__BootstrapServers", "kafka:19092").WithEnvironment("Kafka__GroupId", group)
         .WithEnvironment("Jwt__Issuer", "image-test").WithEnvironment("Jwt__Audience", "image-test").WithEnvironment("Jwt__SigningKey", key)
         .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/api/billing/v1/health/ready"))).Build();
        await api.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri($"http://{api.Hostname}:{api.GetMappedPublicPort(8080)}") };
        Assert.That((await client.GetAsync("/api/billing/v1/cobrancas")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var openapi = await client.GetStringAsync("/openapi/v1.json");
        if (Environment.GetEnvironmentVariable("BILLING_OPENAPI_OUTPUT") is { } output) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!); await File.WriteAllTextAsync(output, openapi); }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = "image-test", Audience = "image-test", Expires = DateTime.UtcNow.AddMinutes(5), SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256) }));
        var order = Guid.NewGuid();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.GetBootstrapAddress(), Acks = Acks.All }).Build();
        var envelope = new { messageId = Guid.NewGuid(), type = "os.pagamento-solicitado", version = 1, occurredAtUtc = DateTimeOffset.UtcNow, correlationId = "image-test", payload = new { ordemServicoId = order, clienteId = Guid.NewGuid(), orcamentoId = Guid.NewGuid(), valor = 0, moeda = "BRL" } };
        await producer.ProduceAsync("oficina.os.billing.requests.v1", new Message<string, string> { Key = order.ToString("D"), Value = JsonSerializer.Serialize(envelope) });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var route = $"/api/billing/v1/ordens/{order}/cobranca";
        while (!(await client.GetAsync(route, timeout.Token)).IsSuccessStatusCode) await Task.Delay(100, timeout.Token);
        var before = await client.GetFromJsonAsync<JsonElement>(route);
        await api.StopAsync(); await api.StartAsync();
        using var restarted = new HttpClient { BaseAddress = new Uri($"http://{api.Hostname}:{api.GetMappedPublicPort(8080)}") };
        restarted.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var after = await restarted.GetFromJsonAsync<JsonElement>(route);
        Assert.That(after.GetProperty("id").GetGuid(), Is.EqualTo(before.GetProperty("id").GetGuid()));
        Assert.That(after.GetProperty("valor").GetDecimal(), Is.Zero);
        var invalidEnvelope = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(envelope))!;
        if (invalidKind == "missing-valor") invalidEnvelope["payload"]!.AsObject().Remove("valor");
        else invalidEnvelope["type"] = "unsupported";
        var invalid = await producer.ProduceAsync("oficina.os.billing.requests.v1", new Message<string, string> { Key = order.ToString("D"), Value = invalidEnvelope.ToJsonString() });
        using var exited = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { while (await api.GetExitCodeAsync(exited.Token) == 0) await Task.Delay(100, exited.Token); }
        catch { var logs = await api.GetLogsAsync(); TestContext.Out.WriteLine(logs.Stdout + logs.Stderr); throw; }
        Assert.That(await api.GetExitCodeAsync(), Is.EqualTo(1));
        using var probe = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = kafka.GetBootstrapAddress(), GroupId = group, EnableAutoCommit = false }).Build();
        Assert.That(probe.Committed(new[] { new TopicPartition("oficina.os.billing.requests.v1", 0) }, TimeSpan.FromSeconds(5)).Single().Offset.Value, Is.EqualTo(invalid.Offset.Value));
    }
}
