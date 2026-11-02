using FIAP.TechChallenge.Billing.API.Security;
using FIAP.TechChallenge.Billing.Application;
using FIAP.TechChallenge.Billing.Infrastructure;
using FIAP.TechChallenge.Billing.Infrastructure.Persistence;
using Maieru.FiapTechChallenge.Chassis;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddChassis(new ChassisOptions { ServiceName = "oficina-billing", EnableOtlp = builder.Configuration.GetValue<bool>("Observability:EnableOtlp") });
builder.Services.AddOpenApi();
builder.Services.AddJwtAuthentication();
builder.Services.AddControllers();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<BillingReadiness>("billing-dependencies", tags: ["ready"]);
var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations")) { await using var scope = app.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Database.MigrateAsync(); }
app.UseChassis();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapChassisHealthChecks("/api/billing/v1/health");
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) { _ = app.MapOpenApi(); _ = app.MapScalarApiReference(); }
await app.RunAsync();
public partial class Program { }
