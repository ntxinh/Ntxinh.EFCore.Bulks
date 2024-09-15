using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateTruncateTableQueryExtensions
{
    public static string GenerateTruncateTableQuery<T>(this DbContext dbContext) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        /* var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var connection = columnMappingsResult.Connection; */

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            /* || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            || connection is null */
        ) return string.Empty;

        // Build query string
        return $"TRUNCATE TABLE {tableName};";
    }
}
