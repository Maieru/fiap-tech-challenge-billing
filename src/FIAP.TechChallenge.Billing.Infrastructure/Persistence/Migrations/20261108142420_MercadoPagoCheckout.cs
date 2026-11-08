using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FIAP.TechChallenge.Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MercadoPagoCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "Cobrancas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "Cobrancas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatewayOrderId",
                table: "Cobrancas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentRequestMessageId",
                table: "Cobrancas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceParent",
                table: "Cobrancas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceState",
                table: "Cobrancas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "Cobrancas");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "Cobrancas");

            migrationBuilder.DropColumn(
                name: "GatewayOrderId",
                table: "Cobrancas");

            migrationBuilder.DropColumn(
                name: "PaymentRequestMessageId",
                table: "Cobrancas");

            migrationBuilder.DropColumn(
                name: "TraceParent",
                table: "Cobrancas");

            migrationBuilder.DropColumn(
                name: "TraceState",
                table: "Cobrancas");
        }
    }
}
