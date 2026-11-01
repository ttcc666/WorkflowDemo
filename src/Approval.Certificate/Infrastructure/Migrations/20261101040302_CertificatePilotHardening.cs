using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Certificate.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CertificatePilotHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedCertNo",
                table: "Certificates",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegisteredBy",
                table: "Certificates",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalInstanceId",
                table: "CertificateRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BeforeSnapshot",
                table: "CertificateRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyStamp",
                table: "CertificateRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "CertificateRequests",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedCertNo",
                table: "CertificateRequests",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestHash",
                table: "CertificateRequests",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceRequestId",
                table: "CertificateRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CertificateNumberReservations",
                columns: table => new
                {
                    NormalizedCertNo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ActiveRequestId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CertificateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CertificateNumberReservations", x => x.NormalizedCertNo);
                    table.CheckConstraint("CK_CertificateNumberReservation_Owner", "\"ActiveRequestId\" IS NOT NULL OR \"CertificateId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_CertificateNumberReservations_CertificateRequests_ActiveRequestId",
                        column: x => x.ActiveRequestId,
                        principalTable: "CertificateRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CertificateNumberReservations_Certificates_CertificateId",
                        column: x => x.CertificateId,
                        principalTable: "Certificates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Certificates_NormalizedCertNo",
                table: "Certificates",
                column: "NormalizedCertNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateRequests_ApplicantId_Operation_IdempotencyKey",
                table: "CertificateRequests",
                columns: new[] { "ApplicantId", "Operation", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateRequests_ApprovalInstanceId",
                table: "CertificateRequests",
                column: "ApprovalInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateRequests_CertificateId_CreatedAt",
                table: "CertificateRequests",
                columns: new[] { "CertificateId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CertificateRequests_SourceRequestId",
                table: "CertificateRequests",
                column: "SourceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_CertificateNumberReservations_ActiveRequestId",
                table: "CertificateNumberReservations",
                column: "ActiveRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CertificateNumberReservations_CertificateId",
                table: "CertificateNumberReservations",
                column: "CertificateId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CertificateRequests_ApprovalInstances_ApprovalInstanceId",
                table: "CertificateRequests",
                column: "ApprovalInstanceId",
                principalTable: "ApprovalInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CertificateRequests_CertificateRequests_SourceRequestId",
                table: "CertificateRequests",
                column: "SourceRequestId",
                principalTable: "CertificateRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CertificateRequests_Certificates_CertificateId",
                table: "CertificateRequests",
                column: "CertificateId",
                principalTable: "Certificates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CertificateRequests_ApprovalInstances_ApprovalInstanceId",
                table: "CertificateRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_CertificateRequests_CertificateRequests_SourceRequestId",
                table: "CertificateRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_CertificateRequests_Certificates_CertificateId",
                table: "CertificateRequests");

            migrationBuilder.DropTable(
                name: "CertificateNumberReservations");

            migrationBuilder.DropIndex(
                name: "IX_Certificates_NormalizedCertNo",
                table: "Certificates");

            migrationBuilder.DropIndex(
                name: "IX_CertificateRequests_ApplicantId_Operation_IdempotencyKey",
                table: "CertificateRequests");

            migrationBuilder.DropIndex(
                name: "IX_CertificateRequests_ApprovalInstanceId",
                table: "CertificateRequests");

            migrationBuilder.DropIndex(
                name: "IX_CertificateRequests_CertificateId_CreatedAt",
                table: "CertificateRequests");

            migrationBuilder.DropIndex(
                name: "IX_CertificateRequests_SourceRequestId",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "NormalizedCertNo",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "RegisteredBy",
                table: "Certificates");

            migrationBuilder.DropColumn(
                name: "ApprovalInstanceId",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "BeforeSnapshot",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "NormalizedCertNo",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "RequestHash",
                table: "CertificateRequests");

            migrationBuilder.DropColumn(
                name: "SourceRequestId",
                table: "CertificateRequests");
        }
    }
}
