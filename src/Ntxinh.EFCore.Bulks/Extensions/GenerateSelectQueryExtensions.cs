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
        // var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            // || connection is null
        ) return string.Empty;

        // Build query string
        var sql = new StringBuilder("SELECT ");
        foreach (var item in columnMappings)
        {
            sql.AppendFormat("[{0}], ", item.SqlColumn.ColumnName);
        }

        sql.Length--;
        sql.Length--;

        sql.AppendFormat("{0}FROM {1};", Constants.NewLine, tableName);

        return sql.ToString();
    }
}
