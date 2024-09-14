using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateCreateTableQueryExtensions
{
    public static string GenerateCreateTableQuery<T>(this DbContext dbContext) where T : class
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
        var sql = new StringBuilder();
        sql.AppendFormat("CREATE TABLE {0} (", tableName);
        sql.AppendFormat("{0}[{1}] {2} IDENTITY(1,1) NOT NULL PRIMARY KEY,", Constants.NewLineAndTab, primaryKeyColumnName.SqlColumn.ColumnName, primaryKeyColumnName.SqlColumn.DataType);

        foreach (var column in columnMappings)
        {
            if (column.SqlColumn.ColumnName == primaryKeyColumnName.SqlColumn.ColumnName)
                continue;

            var isNullable = column.SqlColumn.IsNullable ? "NULL" : "NOT NULL";
            sql.AppendFormat("{0}[{1}] {2} {3},", Constants.NewLineAndTab, column.SqlColumn.ColumnName, column.SqlColumn.DataType, isNullable);
        }

        // Remove the last character ','
        sql.Length--;

        sql.AppendFormat("{0});", Constants.NewLine);

        return sql.ToString();
    }
}
