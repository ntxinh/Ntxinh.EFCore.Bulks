using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateBulkUpdateQueryExtensions
{
    public static string GenerateBulkUpdateQuery<T>(this DbContext dbContext, IEnumerable<T> data, IEnumerable<string> updateColumns) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        // var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        // var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            // || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            // || connection is null
        ) return string.Empty;

        // Build query string
        var sql = new StringBuilder();
        sql.AppendFormat("UPDATE {0}", tableName);
        sql.AppendFormat("{0}SET", Constants.NewLineAndTab);

        ColumnMapDto conditionColumn = null;
        var index = 0;
        foreach (var column in updateColumns)
        {
            index++;
            if (index == 1)
            {
                // First column is condition column
                conditionColumn = columnMappings
                    .FirstOrDefault(x => x.EntityColumn.ColumnName == column);
                continue;
            }
            var updateColumn = columnMappings
                .FirstOrDefault(x => x.EntityColumn.ColumnName == column);
            var isString = new List<string> { "System.String", "System.DateTime" }.Contains(updateColumn.EntityColumn.DataType);
            var singQuote = isString ? "'" : "";

            var caseStatements = string.Join(" ", data.Select(u => $"WHEN {conditionColumn.SqlColumn.ColumnName} = {u.GetPropertyValue(conditionColumn.EntityColumn.ColumnName)} THEN {singQuote}{u.GetPropertyValue(column)}{singQuote}"));

            sql.AppendFormat("{0}{1} = CASE", Constants.NewLine, updateColumn.SqlColumn.ColumnName);
            sql.AppendFormat("{0}{1}", Constants.NewLineAndTab, caseStatements);

            var comma = index == updateColumns.Count() ? "" : ",";
            sql.AppendFormat("{0}END{1}", Constants.NewLineAndTab, comma);
        }

        var idList = string.Join(",", data.Select(u => u.GetPropertyValue(conditionColumn.EntityColumn.ColumnName)));
        sql.AppendFormat("{0}WHERE {1} IN ({2})", Constants.NewLine, conditionColumn.SqlColumn.ColumnName, idList);

        return sql.ToString();
    }
}
