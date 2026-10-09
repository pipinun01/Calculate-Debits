using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Debits.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "shops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    default_due_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reminder_recipients = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    reminder_hour = table.Column<int>(type: "integer", nullable: false, defaultValue: 9),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shops", x => x.id);
                    table.CheckConstraint("ck_shops_default_due_days", "default_due_days > 0");
                    table.CheckConstraint("ck_shops_reminder_hour", "reminder_hour BETWEEN 0 AND 23");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    first_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    active_shop_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_shops_active_shop_id",
                        column: x => x.active_shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "conversation_states",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversation_states", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_conversation_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    message_id = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_entries_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pending_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shop_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notifications_blocked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_shop_members_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_shop_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_events_shop_members_member_id",
                        column: x => x.member_id,
                        principalTable: "shop_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_audit_events_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    search_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_customers_shop_members_created_by_member_id",
                        column: x => x.created_by_member_id,
                        principalTable: "shop_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_customers_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invites", x => x.id);
                    table.ForeignKey(
                        name: "fk_invites_shop_members_created_by_member_id",
                        column: x => x.created_by_member_id,
                        principalTable: "shop_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_invites_shops_shop_id",
                        column: x => x.shop_id,
                        principalTable: "shops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    comment = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    target_debt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    voice_file_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    transcript = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    created_by_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_by_member_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delete_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.CheckConstraint("ck_transactions_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_transactions_debt_has_due_date", "type <> 'debt' OR due_date IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_transactions_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_transactions_shop_members_created_by_member_id",
                        column: x => x.created_by_member_id,
                        principalTable: "shop_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_shop_members_deleted_by_member_id",
                        column: x => x.deleted_by_member_id,
                        principalTable: "shop_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_shop_members_updated_by_member_id",
                        column: x => x.updated_by_member_id,
                        principalTable: "shop_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transactions_transactions_target_debt_id",
                        column: x => x.target_debt_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    debt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_allocations_transactions_debt_id",
                        column: x => x.debt_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payment_allocations_transactions_payment_id",
                        column: x => x.payment_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reminder_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reminder_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_reminder_log_transactions_transaction_id",
                        column: x => x.transaction_id,
                        principalTable: "transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_member_id",
                table: "audit_events",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_shop_id_created_at",
                table: "audit_events",
                columns: new[] { "shop_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_created_by_member_id",
                table: "customers",
                column: "created_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_search_key_trgm",
                table: "customers",
                column: "search_key")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_shop_id_search_key",
                table: "customers",
                columns: new[] { "shop_id", "search_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invites_created_by_member_id",
                table: "invites",
                column: "created_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_invites_shop_id",
                table: "invites",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_invites_token",
                table: "invites",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_debt_id",
                table: "payment_allocations",
                column: "debt_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id_debt_id",
                table: "payment_allocations",
                columns: new[] { "payment_id", "debt_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_entries_shop_id",
                table: "pending_entries",
                column: "shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_pending_entries_status_expires_at",
                table: "pending_entries",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_entries_user_id",
                table: "pending_entries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_log_transaction_id_kind_local_date",
                table: "reminder_log",
                columns: new[] { "transaction_id", "kind", "local_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_members_shop_id_user_id",
                table: "shop_members",
                columns: new[] { "shop_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_members_user_id",
                table: "shop_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_created_by_member_id",
                table: "transactions",
                column: "created_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_customer_id",
                table: "transactions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_deleted_by_member_id",
                table: "transactions",
                column: "deleted_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_open_debts_due",
                table: "transactions",
                columns: new[] { "shop_id", "due_date" },
                filter: "type = 'debt' AND NOT is_deleted");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_shop_id_customer_id_created_at",
                table: "transactions",
                columns: new[] { "shop_id", "customer_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_target_debt_id",
                table: "transactions",
                column: "target_debt_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_updated_by_member_id",
                table: "transactions",
                column: "updated_by_member_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_active_shop_id",
                table: "users",
                column: "active_shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_telegram_user_id",
                table: "users",
                column: "telegram_user_id",
                unique: true);

            migrationBuilder.Sql(Configurations.Views.CreateSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Configurations.Views.DropSql);

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "conversation_states");

            migrationBuilder.DropTable(
                name: "invites");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "pending_entries");

            migrationBuilder.DropTable(
                name: "reminder_log");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "shop_members");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "shops");
        }
    }
}
