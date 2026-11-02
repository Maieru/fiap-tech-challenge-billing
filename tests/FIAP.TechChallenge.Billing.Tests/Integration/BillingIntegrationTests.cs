using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;
using FIAP.TechChallenge.Billing.Infrastructure.Persistence;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;
using FIAP.TechChallenge.OS.Application.Messaging;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Maieru.FiapTechChallenge.Chassis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
namespace FIAP.TechChallenge.Billing.Tests.Integration;

[NonParallelizable]
public class BillingIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private PostgreSqlContainer postgres = null!; private KafkaContainer kafka = null!;
    private readonly string key = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"); private string? previous;
    [OneTimeSetUp]
    public async Task Start()
    {
        postgres = new PostgreSqlBuilder("postgres:18.3").Build(); kafka = new KafkaBuilder("confluentinc/cp-kafka:7.9.2").WithKRaft().Build();
        await Task.WhenAll(postgres.StartAsync(), kafka.StartAsync());
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.GetBootstrapAddress() }).Build();
        await admin.CreateTopicsAsync(new[] { OSMessaging.BillingTopic, "oficina.billing.events.v1" }.Select(x => new TopicSpecification { Name = x, NumPartitions = 1, ReplicationFactor = 1 }));
        previous = Environment.GetEnvironmentVariable("Jwt__SigningKey"); Environment.SetEnvironmentVariable("Jwt__SigningKey", key);
    }
    [OneTimeTearDown] public async Task Stop() { Environment.SetEnvironmentVariable("Jwt__SigningKey", previous); if (kafka is not null) await kafka.DisposeAsync(); if (postgres is not null) await postgres.DisposeAsync(); }
    private WebApplicationFactory<Program> Host(IPaymentGateway? gateway = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Testing"); b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = postgres.GetConnectionString(),
            ["Database:ApplyMigrations"] = "true",
            ["Kafka:BootstrapServers"] = kafka.GetBootstrapAddress(),
            ["Jwt:Issuer"] = "billing-tests",
            ["Jwt:Audience"] = "billing-tests",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        if (gateway is not null) b.ConfigureServices(s => { s.RemoveAll<IPaymentGateway>(); s.AddSingleton(gateway); });
    });
    private async Task<DeliveryResult<string, string>> Send(PagamentoSolicitado request, Guid mid)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.GetBootstrapAddress(), Acks = Acks.All }).Build();
        var envelope = new MessageEnvelope<PagamentoSolicitado>(mid, PagamentoSolicitado.Type, 1, DateTimeOffset.UtcNow, "billing-test", null, null, null, request);
        return await producer.ProduceAsync(OSMessaging.BillingTopic, new Message<string, string> { Key = request.OrdemServicoId.ToString("D"), Value = JsonSerializer.Serialize(envelope, JsonOptions) });
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task ProcessRealMessageAndQueryWithJwt(bool approved)
    {
        var gateway = new CountingGateway(approved);
        await using var host = Host(gateway); using var client = host.CreateClient();
        Assert.That((await client.GetAsync("/api/billing/v1/cobrancas")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = "billing-tests", Audience = "billing-tests", Expires = DateTime.UtcNow.AddMinutes(5), SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256) }));
        Assert.That((await client.GetAsync("/api/billing/v1/cobrancas?pageNumber=2147483647&pageSize=100")).StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await client.GetAsync($"/api/billing/v1/cobrancas/{Guid.NewGuid()}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await client.GetAsync("/api/billing/v1/health/ready")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var request = new PagamentoSolicitado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12.345m, "BRL"); var mid = Guid.NewGuid();
        await Send(request, mid);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40)); JsonElement charge;
        while (true) { using var r = await client.GetAsync($"/api/billing/v1/ordens/{request.OrdemServicoId}/cobranca", timeout.Token); if (r.IsSuccessStatusCode) { charge = await r.Content.ReadFromJsonAsync<JsonElement>(timeout.Token); break; } await Task.Delay(100, timeout.Token); }
        await Send(request, mid);
        var last = await Send(request, Guid.NewGuid());
        using (var probe = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = kafka.GetBootstrapAddress(), GroupId = "oficina-billing-v1", EnableAutoCommit = false }).Build())
        {
            while (probe.Committed(new[] { last.TopicPartition }, TimeSpan.FromSeconds(5)).Single().Offset.Value <= last.Offset.Value)
                await Task.Delay(100, timeout.Token);
        }
        Assert.That((await client.GetAsync("/api/billing/v1/health/ready", timeout.Token)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(gateway.Calls, Is.EqualTo(1));
        Assert.That(CountResults(request.OrdemServicoId, timeout.Token), Is.EqualTo(1));
        var all = await client.GetFromJsonAsync<JsonElement>($"/api/billing/v1/cobrancas?ordemServicoId={request.OrdemServicoId}", timeout.Token);
        Assert.Multiple(() => { Assert.That(charge.GetProperty("valor").GetDecimal(), Is.EqualTo(12.345m)); Assert.That(charge.GetProperty("situacao").GetInt32(), Is.EqualTo(approved ? 1 : 2)); Assert.That(all.GetProperty("totalItems").GetInt32(), Is.EqualTo(1)); });
    }
    private int CountResults(Guid orderId, CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = kafka.GetBootstrapAddress(), GroupId = Guid.NewGuid().ToString("N"), EnableAutoCommit = false }).Build();
        var partition = new TopicPartition("oficina.billing.events.v1", 0);
        var high = consumer.QueryWatermarkOffsets(partition, TimeSpan.FromSeconds(5)).High.Value;
        consumer.Assign(new TopicPartitionOffset(partition, Offset.Beginning));
        var count = 0;
        try
        {
            while (high > 0)
            {
                var record = consumer.Consume(ct);
                if (record.Message.Key == orderId.ToString("D")) count++;
                if (record.Offset.Value >= high - 1) break;
            }
            return count;
        }
        finally { consumer.Close(); }
    }
    [TestCase("gateway")]
    [TestCase("publish")]
    [TestCase("commit")]
    public async Task FailuresRollbackRealDatabase(string failure)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new BillingDbContext(options)) await setup.Database.MigrateAsync();
        var order = Guid.NewGuid(); var mid = Guid.NewGuid();
        if (failure == "commit")
        {
            await using var setup = new BillingDbContext(options);
            await setup.Database.ExecuteSqlRawAsync("CREATE OR REPLACE FUNCTION billing_test_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'injected commit failure'; END $$; CREATE CONSTRAINT TRIGGER billing_test_failure AFTER INSERT ON \"Cobrancas\" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION billing_test_commit_failure();");
        }
        try
        {
            await using (var db = new BillingDbContext(options))
            using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = failure == "publish" ? "127.0.0.1:1" : kafka.GetBootstrapAddress(), MessageTimeoutMs = 1000, Acks = Acks.All, EnableIdempotence = true }).Build())
            {
                var useCase = new ProcessarPagamentoUseCase(db, failure == "gateway" ? new FailingGateway() : new MockPaymentGateway(), new Maieru.FiapTechChallenge.Messaging.Kafka.KafkaMessagingService(producer), TimeProvider.System);
                Assert.That(async () => await useCase.ExecuteAsync(new(mid, "rollback-test", order, Guid.NewGuid(), Guid.NewGuid(), 10, "BRL"), default), Throws.Exception);
            }
            await using var verify = new BillingDbContext(options);
            Assert.Multiple(() => { Assert.That(verify.Cobrancas.Any(x => x.OrdemServicoId == order), Is.False); Assert.That(verify.MensagensProcessadas.Any(x => x.Id == mid), Is.False); });
            if (failure == "commit")
            {
                using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = kafka.GetBootstrapAddress(), GroupId = Guid.NewGuid().ToString(), EnableAutoCommit = false, AutoOffsetReset = AutoOffsetReset.Earliest }).Build();
                consumer.Subscribe("oficina.billing.events.v1"); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (true) { var record = consumer.Consume(timeout.Token); if (record.Message.Key == order.ToString("D")) break; }
                consumer.Close();
            }
        }
        finally
        {
            if (failure == "commit") { await using var cleanup = new BillingDbContext(options); await cleanup.Database.ExecuteSqlRawAsync("DROP TRIGGER billing_test_failure ON \"Cobrancas\"; DROP FUNCTION billing_test_commit_failure();"); }
        }
    }
    [Test]
    public async Task ConcurrentRequestsCreateSingleChargeAndRejectChangedBudget()
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new BillingDbContext(options)) await setup.Database.MigrateAsync();
        var order = Guid.NewGuid(); var customer = Guid.NewGuid(); var budget = Guid.NewGuid(); var gateway = new CountingGateway();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.GetBootstrapAddress(), Acks = Acks.All, EnableIdempotence = true }).Build();
        var messaging = new Maieru.FiapTechChallenge.Messaging.Kafka.KafkaMessagingService(producer);
        async Task Process(decimal value)
        {
            await using var db = new BillingDbContext(options);
            await new ProcessarPagamentoUseCase(db, gateway, messaging, TimeProvider.System).ExecuteAsync(new(Guid.NewGuid(), "concurrent", order, customer, budget, value, "BRL"), default);
        }
        await Task.WhenAll(Process(10), Process(10));
        await using var verify = new BillingDbContext(options);
        Assert.Multiple(() => { Assert.That(verify.Cobrancas.Count(x => x.OrdemServicoId == order), Is.EqualTo(1)); Assert.That(gateway.Calls, Is.EqualTo(1)); });
        Assert.ThrowsAsync<InvalidOperationException>(() => Process(11));
        Assert.That(gateway.Calls, Is.EqualTo(1));
    }
    private sealed class CountingGateway(bool approved = true) : IPaymentGateway
    {
        public int Calls;
        public async Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct)
        { Interlocked.Increment(ref Calls); await Task.Delay(50, ct); return approved ? new(true, "mock-" + request.IdempotencyKey, null) : new(false, null, "recusado"); }
    }
    private sealed class FailingGateway : IPaymentGateway { public Task<PaymentResult> ChargeAsync(PaymentRequest r, CancellationToken ct) => throw new IOException("gateway unavailable"); }
}
