using IoTAgriculture.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTAgriculture.Infrastructure.Migrations
{
    [DbContext(typeof(IoTDbContext))]
    [Migration("202607290001_EnforceUserAccountTypeValues")]
    public partial class EnforceUserAccountTypeValues : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Users]
                SET [AccountType] = CASE
                    WHEN LOWER(LTRIM(RTRIM([AccountType]))) = 'premium' THEN 'premium'
                    ELSE 'standard'
                END;

                IF OBJECT_ID(N'[CK_Users_AccountType]', N'C') IS NULL
                BEGIN
                    ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_AccountType]
                        CHECK ([AccountType] IN ('standard', 'premium'));
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[CK_Users_AccountType]', N'C') IS NOT NULL
                BEGIN
                    ALTER TABLE [Users] DROP CONSTRAINT [CK_Users_AccountType];
                END
                """);
        }
    }
}
