using Confluent.Kafka;
using FIAP.TechChallenge.Billing.Application.Abstractions;
using FIAP.TechChallenge.Billing.Application.Gateways;
using FIAP.TechChallenge.Billing.Domain.Interfaces;
using FIAP.TechChallenge.Billing.Infrastructure.Messaging;
using FIAP.TechChallenge.Billing.Infrastructure.Payments;
using FIAP.TechChallenge.Billing.Infrastructure.Persistence;
using FIAP.TechChallenge.Billing.Infrastructure.Persistence.Repositories;
using Maieru.FiapTechChallenge.Chassis;
using Maieru.FiapTechChallenge.Messaging.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
namespace FIAP.TechChallenge.Billing.Infrastructure;

public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<BillingDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IBillingStore>(sp => sp.GetRequiredService<BillingDbContext>());
        services.AddScoped<ICobrancaQueryRepository, CobrancaQueryRepository>();
        services.AddHttpClient<IPaymentGateway, MercadoPagoPaymentGateway>((sp, http) =>
        {
            // A different endpoint is allowed only for isolated HTTP fixtures in Testing.
            var testUrl = configuration["MercadoPago:BaseUrl"];
            if (testUrl is not null && !sp.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing"))
                throw new InvalidOperationException("MercadoPago:BaseUrl só pode ser alterada em Testing.");
            http.BaseAddress = new Uri(testUrl ?? "https://api.mercadopago.com/");
            http.Timeout = TimeSpan.FromSeconds(8);
        });
        services.AddSingleton<MercadoPagoWebhookSignature>();
        services.AddSingleton<IProducer<string, string>>(_ => new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = configuration["Kafka:BootstrapServers"], ClientId = "oficina-billing", EnableIdempotence = true, Acks = Acks.All, MessageTimeoutMs = 10000 }).Build());
        services.AddSingleton<IMessagingService, KafkaMessagingService>();
        services.AddSingleton<ConsumerState>();
        services.Configure<HostOptions>(o => o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        services.AddHostedService<BillingKafkaConsumer>();
        return services;
    }
}
