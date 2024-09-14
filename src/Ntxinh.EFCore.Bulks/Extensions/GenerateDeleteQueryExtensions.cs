using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateDeleteQueryExtensions
{
    public static (string, ColumnInfoDto) GenerateDeleteQuery<T>(this DbContext dbContext) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        // var columnMappings = columnMappingsResult.ColumnMappings;
        // var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            || primaryKeyColumnName is null
            // || columnMappings is null || !columnMappings.Any()
            // || connection is null
        ) return (string.Empty, null);

        // Build query string
        var sql = $"DELETE FROM {tableName};";
        // var sql = new StringBuilder($"DELETE FROM {tableName}{Constants.NewLine}WHERE [{primaryKeyColumnName.SqlColumnName}] IN @{primaryKeyColumnName.ColumnName};");

        return (sql.ToString(), primaryKeyColumnName.SqlColumn);
    }
}
