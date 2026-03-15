using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Money.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "domain_users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    auth_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    next_category_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_operation_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_place_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_fast_operation_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_regular_operation_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_debt_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_debt_owner_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_car_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    next_car_event_id = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    transporter_password = table.Column<string>(type: "text", nullable: true),
                    transporter_create_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_domain_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cars",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cars", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_cars_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    color = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type_id = table.Column<int>(type: "integer", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_categories_categories_user_id_parent_id",
                        columns: x => new { x.user_id, x.parent_id },
                        principalTable: "categories",
                        principalColumns: new[] { "user_id", "id" });
                    table.ForeignKey(
                        name: "fk_categories_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "debt_owners",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debt_owners", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_debt_owners_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    last_used_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_places_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "car_events",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    car_id = table.Column<int>(type: "integer", nullable: false),
                    type_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    mileage = table.Column<int>(type: "integer", nullable: true),
                    date = table.Column<DateTime>(type: "date", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_car_events", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_car_events_cars_user_id_car_id",
                        columns: x => new { x.user_id, x.car_id },
                        principalTable: "cars",
                        principalColumns: new[] { "user_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_car_events_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fast_operations",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: true),
                    sum = table.Column<decimal>(type: "numeric", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    place_id = table.Column<int>(type: "integer", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fast_operations", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_fast_operations_categories_user_id_category_id",
                        columns: x => new { x.user_id, x.category_id },
                        principalTable: "categories",
                        principalColumns: new[] { "user_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fast_operations_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "operations",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateTime>(type: "date", nullable: false),
                    created_task_id = table.Column<int>(type: "integer", nullable: true),
                    sum = table.Column<decimal>(type: "numeric", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "citext", maxLength: 4000, nullable: true),
                    place_id = table.Column<int>(type: "integer", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operations", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_operations_categories_user_id_category_id",
                        columns: x => new { x.user_id, x.category_id },
                        principalTable: "categories",
                        principalColumns: new[] { "user_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_operations_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "regular_operations",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    time_type_id = table.Column<int>(type: "integer", nullable: false),
                    time_value = table.Column<int>(type: "integer", nullable: true),
                    date_from = table.Column<DateTime>(type: "date", nullable: false),
                    date_to = table.Column<DateTime>(type: "date", nullable: true),
                    run_time = table.Column<DateTime>(type: "date", nullable: true),
                    sum = table.Column<decimal>(type: "numeric", nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    place_id = table.Column<int>(type: "integer", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regular_operations", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_regular_operations_categories_user_id_category_id",
                        columns: x => new { x.user_id, x.category_id },
                        principalTable: "categories",
                        principalColumns: new[] { "user_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_regular_operations_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "debts",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    id = table.Column<int>(type: "integer", nullable: false),
                    type_id = table.Column<int>(type: "integer", nullable: false),
                    sum = table.Column<decimal>(type: "numeric", nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    date = table.Column<DateTime>(type: "date", nullable: false),
                    pay_sum = table.Column<decimal>(type: "numeric", nullable: false),
                    pay_comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    status_id = table.Column<int>(type: "integer", nullable: false),
                    owner_id = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debts", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_debts_debt_owners_user_id_owner_id",
                        columns: x => new { x.user_id, x.owner_id },
                        principalTable: "debt_owners",
                        principalColumns: new[] { "user_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_debts_domain_users_user_id",
                        column: x => x.user_id,
                        principalTable: "domain_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_car_events_user_id_car_id",
                table: "car_events",
                columns: new[] { "user_id", "car_id" });

            migrationBuilder.CreateIndex(
                name: "ix_categories_user_id_parent_id",
                table: "categories",
                columns: new[] { "user_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "ix_debts_user_id_owner_id",
                table: "debts",
                columns: new[] { "user_id", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_fast_operations_user_id_category_id",
                table: "fast_operations",
                columns: new[] { "user_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_operations_comment",
                table: "operations",
                column: "comment");

            migrationBuilder.CreateIndex(
                name: "ix_operations_user_id_category_id",
                table: "operations",
                columns: new[] { "user_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_places_name",
                table: "places",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_regular_operations_user_id_category_id",
                table: "regular_operations",
                columns: new[] { "user_id", "category_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "car_events");

            migrationBuilder.DropTable(
                name: "debts");

            migrationBuilder.DropTable(
                name: "fast_operations");

            migrationBuilder.DropTable(
                name: "operations");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "regular_operations");

            migrationBuilder.DropTable(
                name: "cars");

            migrationBuilder.DropTable(
                name: "debt_owners");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "domain_users");
        }
    }
}
