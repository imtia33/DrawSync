using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrawSync.Migrations
{
    /// <inheritdoc />
    public partial class AddRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Predicate Function
            migrationBuilder.Sql(@"
                CREATE FUNCTION dbo.fn_UserAccessPredicate(@UserId INT)
                    RETURNS TABLE
                    WITH SCHEMABINDING
                AS
                    RETURN SELECT 1 AS fn_access_result
                    WHERE @UserId = CAST(SESSION_CONTEXT(N'UserId') AS INT);
            ");

            // 2. Security Policy for Users table
            migrationBuilder.Sql(@"
                CREATE SECURITY POLICY UserSecurityPolicy
                    ADD FILTER PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users,
                    ADD BLOCK PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users AFTER UPDATE,
                    ADD BLOCK PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users BEFORE DELETE
                    WITH (STATE = ON);
            ");

            // 3. Role Table Lockdown (Allow SELECT only)
            migrationBuilder.Sql(@"
                CREATE FUNCTION dbo.fn_DenyAllPredicate(@obj INT)
                    RETURNS TABLE WITH SCHEMABINDING
                AS
                    RETURN SELECT 0 AS fn_access_result;
            ");
            migrationBuilder.Sql(@"
                CREATE SECURITY POLICY RoleLockdownPolicy
                    ADD BLOCK PREDICATE dbo.fn_DenyAllPredicate(Id) ON dbo.Roles AFTER INSERT,
                    ADD BLOCK PREDICATE dbo.fn_DenyAllPredicate(Id) ON dbo.Roles AFTER UPDATE,
                    ADD BLOCK PREDICATE dbo.fn_DenyAllPredicate(Id) ON dbo.Roles BEFORE DELETE
                    WITH (STATE = ON);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS UserSecurityPolicy;");
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS RoleLockdownPolicy;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_UserAccessPredicate;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS dbo.fn_DenyAllPredicate;");
        }
    }
}
