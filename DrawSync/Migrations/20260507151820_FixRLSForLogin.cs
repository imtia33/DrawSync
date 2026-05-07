using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrawSync.Migrations
{
    /// <inheritdoc />
    public partial class FixRLSForLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS UserSecurityPolicy;");
            migrationBuilder.Sql(@"
                ALTER FUNCTION dbo.fn_UserAccessPredicate(@UserId INT)
                    RETURNS TABLE
                    WITH SCHEMABINDING
                AS
                    RETURN SELECT 1 AS fn_access_result
                    WHERE @UserId = CAST(SESSION_CONTEXT(N'UserId') AS INT)
                       OR SESSION_CONTEXT(N'UserId') IS NULL;
            ");
            migrationBuilder.Sql(@"
                CREATE SECURITY POLICY UserSecurityPolicy
                    ADD FILTER PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users,
                    ADD BLOCK PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users AFTER UPDATE,
                    ADD BLOCK PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users BEFORE DELETE
                    WITH (STATE = ON);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS UserSecurityPolicy;");
            migrationBuilder.Sql(@"
                ALTER FUNCTION dbo.fn_UserAccessPredicate(@UserId INT)
                    RETURNS TABLE
                    WITH SCHEMABINDING
                AS
                    RETURN SELECT 1 AS fn_access_result
                    WHERE @UserId = CAST(SESSION_CONTEXT(N'UserId') AS INT);
            ");
            migrationBuilder.Sql(@"
                CREATE SECURITY POLICY UserSecurityPolicy
                    ADD FILTER PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users,
                    ADD BLOCK PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users AFTER UPDATE,
                    ADD BLOCK PREDICATE dbo.fn_UserAccessPredicate(Id) ON dbo.Users BEFORE DELETE
                    WITH (STATE = ON);
            ");
        }
    }
}
