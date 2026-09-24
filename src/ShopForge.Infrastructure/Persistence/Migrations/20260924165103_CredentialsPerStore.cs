using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopForge.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CredentialsPerStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The new columns arrive empty, are filled from the identity every customer signs in with today, and
            // only then become required. The identity keeps its columns until the copy is done, so nobody is left
            // without a password at any point in the migration.
            migrationBuilder.AddColumn<bool>(
                name: "is_email_verified",
                schema: "customers",
                table: "store_customers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "customers",
                table: "store_customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "customers",
                table: "pending_registrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE customers.store_customers AS c
                SET password_hash = i.password_hash, is_email_verified = i.is_email_verified
                FROM customers.customer_identities AS i
                WHERE i.id = c.customer_identity_id
                """);

            // A registration that was waiting for its link keeps working: the password it will become is the one
            // the identity held when the link was sent.
            migrationBuilder.Sql(
                """
                UPDATE customers.pending_registrations AS r
                SET password_hash = i.password_hash
                FROM customers.customer_identities AS i
                WHERE i.id = r.customer_identity_id
                """);

            // Both are complete or the migration stops here rather than leaving somebody unable to sign in.
            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                schema: "customers",
                table: "store_customers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                schema: "customers",
                table: "pending_registrations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            // A link now belongs to the store that sent it. One without a store could never be used again, so it
            // goes rather than being pointed at a store that does not exist.
            migrationBuilder.Sql("DELETE FROM customers.customer_tokens WHERE store_id IS NULL");

            migrationBuilder.AlterColumn<Guid>(
                name: "store_id",
                schema: "customers",
                table: "customer_tokens",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "is_email_verified",
                schema: "customers",
                table: "customer_identities");

            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "customers",
                table: "customer_identities");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_email_verified",
                schema: "customers",
                table: "customer_identities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                schema: "customers",
                table: "customer_identities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Going back means one password per identity again, so the oldest relationship's wins; a customer who
            // set a different password at another store has to reset it.
            migrationBuilder.Sql(
                """
                UPDATE customers.customer_identities AS i
                SET password_hash = c.password_hash, is_email_verified = c.is_email_verified
                FROM (
                    SELECT DISTINCT ON (customer_identity_id) customer_identity_id, password_hash, is_email_verified
                    FROM customers.store_customers
                    ORDER BY customer_identity_id, id
                ) AS c
                WHERE c.customer_identity_id = i.id
                """);

            migrationBuilder.Sql("UPDATE customers.customer_identities SET password_hash = '' WHERE password_hash IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                schema: "customers",
                table: "customer_identities",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "is_email_verified",
                schema: "customers",
                table: "store_customers");

            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "customers",
                table: "store_customers");

            migrationBuilder.DropColumn(
                name: "password_hash",
                schema: "customers",
                table: "pending_registrations");

            migrationBuilder.AlterColumn<Guid>(
                name: "store_id",
                schema: "customers",
                table: "customer_tokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
