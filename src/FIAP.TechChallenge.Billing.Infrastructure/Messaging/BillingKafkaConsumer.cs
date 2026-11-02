using Confluent.Kafka;
using FIAP.TechChallenge.OS.Application.Messaging;
using Maieru.FiapTechChallenge.Chassis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

using FIAP.TechChallenge.Billing.Application.UseCases.Cobrancas.ProcessarPagamento;
namespace FIAP.TechChallenge.Billing.Infrastructure.Messaging;

public sealed class BillingKafkaConsumer(IConfiguration configuration, IServiceScopeFactory scopes,
    ConsumerState state, ILogger<BillingKafkaConsumer> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"],
            GroupId = configuration["Kafka:GroupId"] ?? "oficina-billing-v1",
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AllowAutoCreateTopics = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        ConsumeResult<string, string>? record = null;
        Guid? pendingMessageId = null;
        string? pendingCorrelationId = null;
        try
        {
            consumer.Subscribe(configuration["Kafka:InputTopic"] ?? OSMessaging.BillingTopic);
            state.Running = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                record = consumer.Consume(stoppingToken);
                pendingMessageId = null;
                pendingCorrelationId = null;
                if (record.IsPartitionEOF)
                    continue;
                var envelope = JsonSerializer.Deserialize<MessageEnvelope<JsonElement>>(record.Message.Value, JsonOptions)
                    ?? throw new InvalidOperationException("Envelope ausente.");
                pendingMessageId = envelope.MessageId;
                pendingCorrelationId = envelope.CorrelationId;
                if (envelope.MessageId == Guid.Empty || envelope.Version != OSMessaging.Version || envelope.OccurredAtUtc == default
                    || envelope.OccurredAtUtc.Offset != TimeSpan.Zero || !ValidCorrelation(envelope.CorrelationId))
                    throw new InvalidOperationException("Metadados de mensagem inválidos.");
                if (!Guid.TryParseExact(record.Message.Key, "D", out var key) || key == Guid.Empty)
                    throw new InvalidOperationException("Chave Kafka inválida.");

                using var logScope = logger.BeginScope(new Dictionary<string, object>
                {
                    ["MessageId"] = envelope.MessageId,
                    ["CorrelationId"] = envelope.CorrelationId,
                    ["Topic"] = record.Topic,
                    ["Partition"] = record.Partition.Value,
                    ["Offset"] = record.Offset.Value
                });
                using var activity = new Activity("billing.kafka.consume").SetIdFormat(ActivityIdFormat.W3C);
                if (envelope.TraceParent is not null)
                    _ = activity.SetParentId(envelope.TraceParent);
                activity.TraceStateString = envelope.TraceState;
                _ = activity.AddBaggage("correlation.id", envelope.CorrelationId);
                _ = activity.Start();
                await using var scope = scopes.CreateAsyncScope();
                switch (envelope.Type, envelope.Version)
                {
                    case (PagamentoSolicitado.Type, OSMessaging.Version):
                        MonetaryPayloadValidation.RequireDecimal(envelope.Payload, "valor");
                        var p = Payload<PagamentoSolicitado>(envelope); CheckKey(key, p.OrdemServicoId);
                        await scope.ServiceProvider.GetRequiredService<IProcessarPagamentoUseCase>().ExecuteAsync(new(envelope.MessageId, envelope.CorrelationId, p.OrdemServicoId, p.ClienteId, p.OrcamentoId, p.Valor, p.Moeda), stoppingToken);
                        break;
                    default: throw new InvalidOperationException("Tipo ou versão não suportado.");
                }
                consumer.Commit(record);
                logger.LogInformation("Mensagem processada e offset confirmado.");
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Critical))
            {
                logger.LogCritical(exception, "Consumo interrompido sem avançar o offset pendente. Tópico {Topic}, partição {Partition}, offset {Offset}, mensagem {MessageId}, correlação {CorrelationId}. Corrija a causa e reinicie.",
                    record?.Topic, record?.Partition.Value, record?.Offset.Value, pendingMessageId, pendingCorrelationId);
            }
            Environment.ExitCode = 1;
            throw;
        }
        finally
        {
            state.Running = false;
            try
            { consumer.Close(); }
            catch (KafkaException exception) { logger.LogError(exception, "Falha ao fechar o consumidor."); }
        }
    }

    private static T Payload<T>(MessageEnvelope<JsonElement> envelope) =>
        envelope.Payload.Deserialize<T>(JsonOptions) ?? throw new InvalidOperationException("Payload ausente.");
    private static void CheckKey(Guid key, Guid id)
    {
        if (key != id)
            throw new InvalidOperationException("A chave Kafka não corresponde à ordem do payload.");
    }
    private static bool ValidCorrelation(string? value) => value is { Length: > 0 and <= 128 }
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}