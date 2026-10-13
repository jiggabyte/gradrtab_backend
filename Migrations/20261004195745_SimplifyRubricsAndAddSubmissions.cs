using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GradrTab.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyRubricsAndAddSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RubricLevels");

            migrationBuilder.DropTable(
                name: "RubricCriteria");

            migrationBuilder.DropIndex(
                name: "IX_Rubrics_UploadedByUserId_RubricKey",
                table: "Rubrics");

            migrationBuilder.DropColumn(
                name: "CriterionCount",
                table: "Rubrics");

            migrationBuilder.DropColumn(
                name: "MaxPoints",
                table: "Rubrics");

            migrationBuilder.RenameColumn(
                name: "Title",
                table: "Rubrics",
                newName: "CourseName");

            migrationBuilder.RenameColumn(
                name: "RubricKey",
                table: "Rubrics",
                newName: "CourseId");

            migrationBuilder.AddColumn<string>(
                name: "RubricContent",
                table: "Rubrics",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RubricId = table.Column<Guid>(type: "uuid", nullable: true),
                    StudentId = table.Column<string>(type: "text", nullable: false),
                    StudentName = table.Column<string>(type: "text", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Evaluation = table.Column<string>(type: "jsonb", nullable: true),
                    GradingStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Submissions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Submissions_Rubrics_RubricId",
                        column: x => x.RubricId,
                        principalTable: "Rubrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Submissions_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rubrics_UploadedByUserId",
                table: "Rubrics",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_CreatedAt",
                table: "Submissions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_DocumentId",
                table: "Submissions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_RubricId_CreatedAt",
                table: "Submissions",
                columns: new[] { "RubricId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_StudentId_CreatedAt",
                table: "Submissions",
                columns: new[] { "StudentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_UploadedByUserId",
                table: "Submissions",
                column: "UploadedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropIndex(
                name: "IX_Rubrics_UploadedByUserId",
                table: "Rubrics");

            migrationBuilder.DropColumn(
                name: "RubricContent",
                table: "Rubrics");

            migrationBuilder.RenameColumn(
                name: "CourseName",
                table: "Rubrics",
                newName: "Title");

            migrationBuilder.RenameColumn(
                name: "CourseId",
                table: "Rubrics",
                newName: "RubricKey");

            migrationBuilder.AddColumn<int>(
                name: "CriterionCount",
                table: "Rubrics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxPoints",
                table: "Rubrics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RubricCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RubricId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaxPoints = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricCriteria_Rubrics_RubricId",
                        column: x => x.RubricId,
                        principalTable: "Rubrics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RubricLevels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CriterionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RubricLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RubricLevels_RubricCriteria_CriterionId",
                        column: x => x.CriterionId,
                        principalTable: "RubricCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rubrics_UploadedByUserId_RubricKey",
                table: "Rubrics",
                columns: new[] { "UploadedByUserId", "RubricKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RubricCriteria_RubricId_Order",
                table: "RubricCriteria",
                columns: new[] { "RubricId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_RubricLevels_CriterionId_Order",
                table: "RubricLevels",
                columns: new[] { "CriterionId", "Order" });
        }
    }
}
