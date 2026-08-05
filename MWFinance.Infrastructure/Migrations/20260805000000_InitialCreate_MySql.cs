using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pomelo.EntityFrameworkCore.MySql.Metadata;

#nullable disable

namespace MWFinance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate_MySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DirectDebitAuthorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CustomerIdNumber = table.Column<string>(type: "longtext", nullable: false),
                    CustomerFullName = table.Column<string>(type: "longtext", nullable: false),
                    CustomerMobileNumber = table.Column<string>(type: "longtext", nullable: false),
                    CustomerEmail = table.Column<string>(type: "longtext", nullable: false),
                    CustomerType = table.Column<string>(type: "longtext", nullable: false),
                    CustomerIdType = table.Column<string>(type: "longtext", nullable: false),
                    CustNid = table.Column<string>(type: "longtext", nullable: false),
                    DdaReferenceNumber = table.Column<string>(type: "longtext", nullable: false),
                    CommencesOn = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresOn = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MinAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentFrequency = table.Column<string>(type: "longtext", nullable: false),
                    AmountType = table.Column<string>(type: "longtext", nullable: false),
                    UserPreferPaymentMethod = table.Column<string>(type: "longtext", nullable: false),
                    CustomerAccountBankName = table.Column<string>(type: "longtext", nullable: false),
                    CustomerBankAccountTitle = table.Column<string>(type: "longtext", nullable: true),
                    CustomerBankAccountType = table.Column<string>(type: "longtext", nullable: true),
                    CustomerBankAccountNumber = table.Column<string>(type: "longtext", nullable: true),
                    CustomerCreditCardNumber = table.Column<string>(type: "longtext", nullable: true),
                    CreditCardHolderName = table.Column<string>(type: "longtext", nullable: true),
                    DdarId = table.Column<int>(type: "int", nullable: true),
                    DdaId = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DdaStatus = table.Column<string>(type: "longtext", nullable: false),
                    CentralBankRefNumber = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DirectDebitAuthorities", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FintechClienstApi",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    ClientSecretHash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    CompanyName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FintechClienstApi", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_FintechClienstApi_ClientId",
                table: "FintechClienstApi",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DirectDebitAuthorities");

            migrationBuilder.DropTable(
                name: "FintechClienstApi");
        }
    }
}
