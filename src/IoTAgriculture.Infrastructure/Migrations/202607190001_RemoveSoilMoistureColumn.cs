using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IoTAgriculture.Infrastructure.Migrations
{
    public partial class RemoveSoilMoistureColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @sql nvarchar(max) = N'';

                SELECT @sql = @sql + N'DROP INDEX ' +
                    QUOTENAME(i.name) + N' ON ' +
                    QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) + N';' + CHAR(13)
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic
                    ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                INNER JOIN sys.columns c
                    ON ic.object_id = c.object_id AND ic.column_id = c.column_id
                INNER JOIN sys.tables t
                    ON c.object_id = t.object_id
                WHERE c.name = N'SoilMoisture'
                    AND i.is_primary_key = 0
                    AND i.is_unique_constraint = 0;

                SELECT @sql = @sql + N'ALTER TABLE ' +
                    QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) +
                    N' DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';' + CHAR(13)
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c
                    ON dc.parent_object_id = c.object_id
                    AND dc.parent_column_id = c.column_id
                INNER JOIN sys.tables t
                    ON c.object_id = t.object_id
                WHERE c.name = N'SoilMoisture';

                SELECT @sql = @sql + N'ALTER TABLE ' +
                    QUOTENAME(SCHEMA_NAME(t.schema_id)) + N'.' + QUOTENAME(t.name) +
                    N' DROP COLUMN ' + QUOTENAME(c.name) + N';' + CHAR(13)
                FROM sys.columns c
                INNER JOIN sys.tables t
                    ON c.object_id = t.object_id
                WHERE c.name = N'SoilMoisture';

                IF @sql <> N''
                BEGIN
                    EXEC sp_executesql @sql;
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
