using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dogo.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDogMembersAndInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dog_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dog_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dog_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_dog_invitations_dogs_dog_id",
                        column: x => x.dog_id,
                        principalTable: "dogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dog_invitations_users_created_by_id",
                        column: x => x.created_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_dog_invitations_users_used_by_id",
                        column: x => x.used_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "dog_members",
                columns: table => new
                {
                    dog_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dog_members", x => new { x.dog_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_dog_members_dogs_dog_id",
                        column: x => x.dog_id,
                        principalTable: "dogs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_dog_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dog_invitations_created_by_id",
                table: "dog_invitations",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dog_invitations_dog_id",
                table: "dog_invitations",
                column: "dog_id");

            migrationBuilder.CreateIndex(
                name: "ix_dog_invitations_token_hash",
                table: "dog_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dog_invitations_used_by_id",
                table: "dog_invitations",
                column: "used_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_dog_members_user_id",
                table: "dog_members",
                column: "user_id");

            // Existing dogs (local databases; the V1 is not deployed): their single owner becomes
            // their first Owner link, dated like the dog (ADR 0042).
            migrationBuilder.Sql(
                """
                INSERT INTO dog_members (dog_id, user_id, role, created_at)
                SELECT id, owner_id, 'Owner', created_at FROM dogs;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_dogs_users_owner_id",
                table: "dogs");

            migrationBuilder.DropIndex(
                name: "ix_dogs_owner_id",
                table: "dogs");

            migrationBuilder.DropColumn(
                name: "owner_id",
                table: "dogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to a single owner: the oldest Owner link of each dog. A dog left without any
            // owner cannot be represented and is deleted. Invitations are lost.
            migrationBuilder.AddColumn<Guid>(
                name: "owner_id",
                table: "dogs",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE dogs d SET owner_id = (
                    SELECT m.user_id FROM dog_members m
                    WHERE m.dog_id = d.id AND m.role = 'Owner'
                    ORDER BY m.created_at, m.user_id
                    LIMIT 1);
                DELETE FROM dogs WHERE owner_id IS NULL;
                ALTER TABLE dogs ALTER COLUMN owner_id SET NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "dog_invitations");

            migrationBuilder.DropTable(
                name: "dog_members");

            migrationBuilder.CreateIndex(
                name: "ix_dogs_owner_id",
                table: "dogs",
                column: "owner_id");

            migrationBuilder.AddForeignKey(
                name: "fk_dogs_users_owner_id",
                table: "dogs",
                column: "owner_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
