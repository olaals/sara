using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RefactorWorkflowSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlantData_Anonymization_AnonymizationId",
                table: "PlantData");

            migrationBuilder.DropForeignKey(
                name: "FK_PlantData_CLOEAnalysis_CLOEAnalysisId",
                table: "PlantData");

            migrationBuilder.DropForeignKey(
                name: "FK_PlantData_FencillaAnalysis_FencillaAnalysisId",
                table: "PlantData");

            migrationBuilder.DropForeignKey(
                name: "FK_PlantData_ThermalReading_ThermalReadingAnalysisId",
                table: "PlantData");

            migrationBuilder.DropIndex(
                name: "IX_PlantData_AnonymizationId",
                table: "PlantData");

            migrationBuilder.DropIndex(
                name: "IX_PlantData_CLOEAnalysisId",
                table: "PlantData");

            migrationBuilder.DropIndex(
                name: "IX_PlantData_FencillaAnalysisId",
                table: "PlantData");

            migrationBuilder.DropIndex(
                name: "IX_PlantData_ThermalReadingAnalysisId",
                table: "PlantData");

            migrationBuilder.RenameColumn(
                name: "ThermalReadingAnalysisId",
                table: "PlantData",
                newName: "WorkflowId");

            migrationBuilder.CreateTable(
                name: "Workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflow", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowStep",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SourceBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowStep", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowStep_Workflow_WorkflowId",
                        column: x => x.WorkflowId,
                        principalTable: "Workflow",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AnonymizationData",
                columns: table => new
                {
                    WorkflowStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPersonInImage = table.Column<bool>(type: "boolean", nullable: true),
                    PreProcessedBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: true),
                    PreProcessedBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: true),
                    PreProcessedBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnonymizationData", x => x.WorkflowStepId);
                    table.ForeignKey(
                        name: "FK_AnonymizationData_WorkflowStep_WorkflowStepId",
                        column: x => x.WorkflowStepId,
                        principalTable: "WorkflowStep",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CLOEData",
                columns: table => new
                {
                    WorkflowStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    OilLevel = table.Column<float>(type: "real", nullable: true),
                    Confidence = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLOEData", x => x.WorkflowStepId);
                    table.ForeignKey(
                        name: "FK_CLOEData_WorkflowStep_WorkflowStepId",
                        column: x => x.WorkflowStepId,
                        principalTable: "WorkflowStep",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FencillaData",
                columns: table => new
                {
                    WorkflowStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsBreak = table.Column<bool>(type: "boolean", nullable: true),
                    Confidence = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FencillaData", x => x.WorkflowStepId);
                    table.ForeignKey(
                        name: "FK_FencillaData_WorkflowStep_WorkflowStepId",
                        column: x => x.WorkflowStepId,
                        principalTable: "WorkflowStep",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ThermalReadingData",
                columns: table => new
                {
                    WorkflowStepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Temperature = table.Column<float>(type: "real", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThermalReadingData", x => x.WorkflowStepId);
                    table.ForeignKey(
                        name: "FK_ThermalReadingData_WorkflowStep_WorkflowStepId",
                        column: x => x.WorkflowStepId,
                        principalTable: "WorkflowStep",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""Workflow"" (""Id"")
                SELECT DISTINCT p.""AnonymizationId""
                FROM public.""PlantData"" p
                WHERE p.""AnonymizationId"" IS NOT NULL;
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""WorkflowStep"" (
                    ""Id"",
                    ""WorkflowId"",
                    ""Type"",
                    ""SourceBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DateCreated"",
                    ""Status""
                )
                SELECT
                    a.""Id"",
                    p.""AnonymizationId"",
                    0,
                    a.""SourceBlobStorageLocation_StorageAccount"",
                    a.""SourceBlobStorageLocation_BlobContainer"",
                    a.""SourceBlobStorageLocation_BlobName"",
                    a.""DestinationBlobStorageLocation_StorageAccount"",
                    a.""DestinationBlobStorageLocation_BlobContainer"",
                    a.""DestinationBlobStorageLocation_BlobName"",
                    a.""DateCreated"",
                    a.""Status""
                FROM public.""Anonymization"" a
                INNER JOIN public.""PlantData"" p ON p.""AnonymizationId"" = a.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""AnonymizationData"" (
                    ""WorkflowStepId"",
                    ""IsPersonInImage"",
                    ""PreProcessedBlobStorageLocation_StorageAccount"",
                    ""PreProcessedBlobStorageLocation_BlobContainer"",
                    ""PreProcessedBlobStorageLocation_BlobName""
                )
                SELECT
                    a.""Id"",
                    a.""IsPersonInImage"",
                    a.""PreProcessedBlobStorageLocation_StorageAccount"",
                    a.""PreProcessedBlobStorageLocation_BlobContainer"",
                    a.""PreProcessedBlobStorageLocation_BlobName""
                FROM public.""Anonymization"" a
                INNER JOIN public.""PlantData"" p ON p.""AnonymizationId"" = a.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""WorkflowStep"" (
                    ""Id"",
                    ""WorkflowId"",
                    ""Type"",
                    ""SourceBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DateCreated"",
                    ""Status""
                )
                SELECT
                    c.""Id"",
                    p.""AnonymizationId"",
                    1,
                    c.""SourceBlobStorageLocation_StorageAccount"",
                    c.""SourceBlobStorageLocation_BlobContainer"",
                    c.""SourceBlobStorageLocation_BlobName"",
                    c.""DestinationBlobStorageLocation_StorageAccount"",
                    c.""DestinationBlobStorageLocation_BlobContainer"",
                    c.""DestinationBlobStorageLocation_BlobName"",
                    c.""DateCreated"",
                    c.""Status""
                FROM public.""CLOEAnalysis"" c
                INNER JOIN public.""PlantData"" p ON p.""CLOEAnalysisId"" = c.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""CLOEData"" (""WorkflowStepId"", ""OilLevel"", ""Confidence"")
                SELECT c.""Id"", c.""OilLevel"", c.""Confidence""
                FROM public.""CLOEAnalysis"" c
                INNER JOIN public.""PlantData"" p ON p.""CLOEAnalysisId"" = c.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""WorkflowStep"" (
                    ""Id"",
                    ""WorkflowId"",
                    ""Type"",
                    ""SourceBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DateCreated"",
                    ""Status""
                )
                SELECT
                    f.""Id"",
                    p.""AnonymizationId"",
                    2,
                    f.""SourceBlobStorageLocation_StorageAccount"",
                    f.""SourceBlobStorageLocation_BlobContainer"",
                    f.""SourceBlobStorageLocation_BlobName"",
                    f.""DestinationBlobStorageLocation_StorageAccount"",
                    f.""DestinationBlobStorageLocation_BlobContainer"",
                    f.""DestinationBlobStorageLocation_BlobName"",
                    f.""DateCreated"",
                    f.""Status""
                FROM public.""FencillaAnalysis"" f
                INNER JOIN public.""PlantData"" p ON p.""FencillaAnalysisId"" = f.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""FencillaData"" (""WorkflowStepId"", ""IsBreak"", ""Confidence"")
                SELECT f.""Id"", f.""IsBreak"", f.""Confidence""
                FROM public.""FencillaAnalysis"" f
                INNER JOIN public.""PlantData"" p ON p.""FencillaAnalysisId"" = f.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""WorkflowStep"" (
                    ""Id"",
                    ""WorkflowId"",
                    ""Type"",
                    ""SourceBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DateCreated"",
                    ""Status""
                )
                SELECT
                    t.""Id"",
                    p.""AnonymizationId"",
                    3,
                    t.""SourceBlobStorageLocation_StorageAccount"",
                    t.""SourceBlobStorageLocation_BlobContainer"",
                    t.""SourceBlobStorageLocation_BlobName"",
                    t.""DestinationBlobStorageLocation_StorageAccount"",
                    t.""DestinationBlobStorageLocation_BlobContainer"",
                    t.""DestinationBlobStorageLocation_BlobName"",
                    t.""DateCreated"",
                    t.""Status""
                FROM public.""ThermalReading"" t
                INNER JOIN public.""PlantData"" p ON p.""WorkflowId"" = t.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""ThermalReadingData"" (""WorkflowStepId"", ""Temperature"")
                SELECT t.""Id"", t.""Temperature""
                FROM public.""ThermalReading"" t
                INNER JOIN public.""PlantData"" p ON p.""WorkflowId"" = t.""Id"";
                "
            );

            migrationBuilder.Sql(
                @"
                UPDATE public.""PlantData""
                SET ""WorkflowId"" = ""AnonymizationId"";
                "
            );

            migrationBuilder.CreateIndex(
                name: "IX_PlantData_WorkflowId",
                table: "PlantData",
                column: "WorkflowId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowStep_WorkflowId_Type",
                table: "WorkflowStep",
                columns: new[] { "WorkflowId", "Type" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlantData_Workflow_WorkflowId",
                table: "PlantData",
                column: "WorkflowId",
                principalTable: "Workflow",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "Anonymization");

            migrationBuilder.DropTable(
                name: "CLOEAnalysis");

            migrationBuilder.DropTable(
                name: "FencillaAnalysis");

            migrationBuilder.DropTable(
                name: "ThermalReading");

            migrationBuilder.DropColumn(
                name: "AnonymizationId",
                table: "PlantData");

            migrationBuilder.DropColumn(
                name: "CLOEAnalysisId",
                table: "PlantData");

            migrationBuilder.DropColumn(
                name: "FencillaAnalysisId",
                table: "PlantData");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlantData_Workflow_WorkflowId",
                table: "PlantData");

            migrationBuilder.DropIndex(
                name: "IX_PlantData_WorkflowId",
                table: "PlantData");

            migrationBuilder.AddColumn<Guid>(
                name: "AnonymizationId",
                table: "PlantData",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CLOEAnalysisId",
                table: "PlantData",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FencillaAnalysisId",
                table: "PlantData",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Anonymization",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsPersonInImage = table.Column<bool>(type: "boolean", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DestinationBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false),
                    PreProcessedBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: true),
                    PreProcessedBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: true),
                    PreProcessedBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: true),
                    SourceBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anonymization", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CLOEAnalysis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OilLevel = table.Column<float>(type: "real", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DestinationBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CLOEAnalysis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FencillaAnalysis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsBreak = table.Column<bool>(type: "boolean", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DestinationBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FencillaAnalysis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ThermalReading",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Temperature = table.Column<float>(type: "real", nullable: true),
                    DestinationBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    DestinationBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobContainer = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_BlobName = table.Column<string>(type: "text", nullable: false),
                    SourceBlobStorageLocation_StorageAccount = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThermalReading", x => x.Id);
                });

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""Anonymization"" (
                    ""Id"",
                    ""DateCreated"",
                    ""IsPersonInImage"",
                    ""Status"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""PreProcessedBlobStorageLocation_BlobContainer"",
                    ""PreProcessedBlobStorageLocation_BlobName"",
                    ""PreProcessedBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""SourceBlobStorageLocation_StorageAccount""
                )
                SELECT
                    ws.""Id"",
                    ws.""DateCreated"",
                    ad.""IsPersonInImage"",
                    ws.""Status"",
                    ws.""DestinationBlobStorageLocation_BlobContainer"",
                    ws.""DestinationBlobStorageLocation_BlobName"",
                    ws.""DestinationBlobStorageLocation_StorageAccount"",
                    ad.""PreProcessedBlobStorageLocation_BlobContainer"",
                    ad.""PreProcessedBlobStorageLocation_BlobName"",
                    ad.""PreProcessedBlobStorageLocation_StorageAccount"",
                    ws.""SourceBlobStorageLocation_BlobContainer"",
                    ws.""SourceBlobStorageLocation_BlobName"",
                    ws.""SourceBlobStorageLocation_StorageAccount""
                FROM public.""WorkflowStep"" ws
                LEFT JOIN public.""AnonymizationData"" ad ON ad.""WorkflowStepId"" = ws.""Id""
                WHERE ws.""Type"" = 0;
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""CLOEAnalysis"" (
                    ""Id"",
                    ""Confidence"",
                    ""DateCreated"",
                    ""OilLevel"",
                    ""Status"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""SourceBlobStorageLocation_StorageAccount""
                )
                SELECT
                    ws.""Id"",
                    cd.""Confidence"",
                    ws.""DateCreated"",
                    cd.""OilLevel"",
                    ws.""Status"",
                    ws.""DestinationBlobStorageLocation_BlobContainer"",
                    ws.""DestinationBlobStorageLocation_BlobName"",
                    ws.""DestinationBlobStorageLocation_StorageAccount"",
                    ws.""SourceBlobStorageLocation_BlobContainer"",
                    ws.""SourceBlobStorageLocation_BlobName"",
                    ws.""SourceBlobStorageLocation_StorageAccount""
                FROM public.""WorkflowStep"" ws
                LEFT JOIN public.""CLOEData"" cd ON cd.""WorkflowStepId"" = ws.""Id""
                WHERE ws.""Type"" = 1;
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""FencillaAnalysis"" (
                    ""Id"",
                    ""Confidence"",
                    ""DateCreated"",
                    ""IsBreak"",
                    ""Status"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""SourceBlobStorageLocation_StorageAccount""
                )
                SELECT
                    ws.""Id"",
                    fd.""Confidence"",
                    ws.""DateCreated"",
                    fd.""IsBreak"",
                    ws.""Status"",
                    ws.""DestinationBlobStorageLocation_BlobContainer"",
                    ws.""DestinationBlobStorageLocation_BlobName"",
                    ws.""DestinationBlobStorageLocation_StorageAccount"",
                    ws.""SourceBlobStorageLocation_BlobContainer"",
                    ws.""SourceBlobStorageLocation_BlobName"",
                    ws.""SourceBlobStorageLocation_StorageAccount""
                FROM public.""WorkflowStep"" ws
                LEFT JOIN public.""FencillaData"" fd ON fd.""WorkflowStepId"" = ws.""Id""
                WHERE ws.""Type"" = 2;
                "
            );

            migrationBuilder.Sql(
                @"
                INSERT INTO public.""ThermalReading"" (
                    ""Id"",
                    ""DateCreated"",
                    ""Status"",
                    ""Temperature"",
                    ""DestinationBlobStorageLocation_BlobContainer"",
                    ""DestinationBlobStorageLocation_BlobName"",
                    ""DestinationBlobStorageLocation_StorageAccount"",
                    ""SourceBlobStorageLocation_BlobContainer"",
                    ""SourceBlobStorageLocation_BlobName"",
                    ""SourceBlobStorageLocation_StorageAccount""
                )
                SELECT
                    ws.""Id"",
                    ws.""DateCreated"",
                    ws.""Status"",
                    td.""Temperature"",
                    ws.""DestinationBlobStorageLocation_BlobContainer"",
                    ws.""DestinationBlobStorageLocation_BlobName"",
                    ws.""DestinationBlobStorageLocation_StorageAccount"",
                    ws.""SourceBlobStorageLocation_BlobContainer"",
                    ws.""SourceBlobStorageLocation_BlobName"",
                    ws.""SourceBlobStorageLocation_StorageAccount""
                FROM public.""WorkflowStep"" ws
                LEFT JOIN public.""ThermalReadingData"" td ON td.""WorkflowStepId"" = ws.""Id""
                WHERE ws.""Type"" = 3;
                "
            );

            migrationBuilder.Sql(
                @"
                UPDATE public.""PlantData""
                SET ""AnonymizationId"" = ""WorkflowId"";
                "
            );

            migrationBuilder.Sql(
                @"
                UPDATE public.""PlantData"" p
                SET ""CLOEAnalysisId"" = ws.""Id""
                FROM public.""WorkflowStep"" ws
                WHERE ws.""WorkflowId"" = p.""WorkflowId""
                AND ws.""Type"" = 1;
                "
            );

            migrationBuilder.Sql(
                @"
                UPDATE public.""PlantData"" p
                SET ""FencillaAnalysisId"" = ws.""Id""
                FROM public.""WorkflowStep"" ws
                WHERE ws.""WorkflowId"" = p.""WorkflowId""
                AND ws.""Type"" = 2;
                "
            );

            migrationBuilder.Sql(
                @"
                UPDATE public.""PlantData""
                SET ""WorkflowId"" = NULL;
                "
            );

            migrationBuilder.Sql(
                @"
                UPDATE public.""PlantData"" p
                SET ""WorkflowId"" = ws.""Id""
                FROM public.""WorkflowStep"" ws
                WHERE ws.""WorkflowId"" = p.""AnonymizationId""
                AND ws.""Type"" = 3;
                "
            );

            migrationBuilder.DropTable(
                name: "AnonymizationData");

            migrationBuilder.DropTable(
                name: "CLOEData");

            migrationBuilder.DropTable(
                name: "FencillaData");

            migrationBuilder.DropTable(
                name: "ThermalReadingData");

            migrationBuilder.DropTable(
                name: "WorkflowStep");

            migrationBuilder.DropTable(
                name: "Workflow");

            migrationBuilder.RenameColumn(
                name: "WorkflowId",
                table: "PlantData",
                newName: "ThermalReadingAnalysisId");

            migrationBuilder.AlterColumn<Guid>(
                name: "AnonymizationId",
                table: "PlantData",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlantData_AnonymizationId",
                table: "PlantData",
                column: "AnonymizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantData_CLOEAnalysisId",
                table: "PlantData",
                column: "CLOEAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantData_FencillaAnalysisId",
                table: "PlantData",
                column: "FencillaAnalysisId");

            migrationBuilder.CreateIndex(
                name: "IX_PlantData_ThermalReadingAnalysisId",
                table: "PlantData",
                column: "ThermalReadingAnalysisId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlantData_Anonymization_AnonymizationId",
                table: "PlantData",
                column: "AnonymizationId",
                principalTable: "Anonymization",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PlantData_CLOEAnalysis_CLOEAnalysisId",
                table: "PlantData",
                column: "CLOEAnalysisId",
                principalTable: "CLOEAnalysis",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PlantData_FencillaAnalysis_FencillaAnalysisId",
                table: "PlantData",
                column: "FencillaAnalysisId",
                principalTable: "FencillaAnalysis",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PlantData_ThermalReading_ThermalReadingAnalysisId",
                table: "PlantData",
                column: "ThermalReadingAnalysisId",
                principalTable: "ThermalReading",
                principalColumn: "Id");
        }
    }
}
