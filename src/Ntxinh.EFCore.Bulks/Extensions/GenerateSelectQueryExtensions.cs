using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateSelectQueryExtensions
{
    public static string GenerateSelectQuery<T>(this DbContext dbContext) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            || primaryKeyColumnName is null || primaryKeyColumnName.Equals(default(KeyValuePair<string, string>))
            || columnMappings is null || !columnMappings.Any()
            || connection is null
        ) return string.Empty;

        // Build query string
        var sql = new StringBuilder();
        foreach (var item in columnMappings)
        {
            if (sql.Length > 0)
            {
                sql.Append(", ");
            }
            sql.Append("[");
            sql.Append(item.Value);
            sql.Append("]");
        }

        return sql.ToString();
    }
}
