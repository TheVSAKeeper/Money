using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Money.Data.Migrations.Routing
{
    /// <inheritdoc />
    public partial class FixShardMappingCompositeKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_shard_mappings",
                table: "shard_mappings");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "shard_mappings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_shard_mappings",
                table: "shard_mappings",
                columns: new[] { "user_id", "shard_name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_shard_mappings",
                table: "shard_mappings");

            migrationBuilder.AlterColumn<int>(
                name: "user_id",
                table: "shard_mappings",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_shard_mappings",
                table: "shard_mappings",
                column: "user_id");
        }
    }
}
