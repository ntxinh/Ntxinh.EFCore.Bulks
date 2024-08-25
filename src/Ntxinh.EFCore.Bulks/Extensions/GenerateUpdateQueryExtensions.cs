using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateUpdateQueryExtensions
{
    public static string GenerateUpdateQuery<T>(this DbContext dbContext) where T : class
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

        var dataTable = DataTableHelper.CreateDataTable<T>();

        // Build query string
        var sql = new StringBuilder($"UPDATE {tableName} SET ");
        var values = new StringBuilder();
        var where = string.Empty;
        bool bFirst = true;

        foreach (DataColumn column in dataTable.Columns)
        {
            if (!column.AutoIncrement
                && primaryKeyColumnName is not null
                && !primaryKeyColumnName.Equals(default(KeyValuePair<string, string>))
                && column.ColumnName == primaryKeyColumnName.Value.Key)
            {
                column.AutoIncrement = true;
            }

            var newColumnName = column.ColumnName;
            var columnMapping = columnMappings.FirstOrDefault(x => x.Key == column.ColumnName);
            if (!columnMapping.Equals(default(KeyValuePair<string, string>)))
            {
                newColumnName = columnMapping.Value;
            }
            if (string.IsNullOrEmpty(newColumnName)) continue;

            if (column.AutoIncrement)
            {
                where = $" WHERE {primaryKeyColumnName.Value.Value}=@{primaryKeyColumnName.Value.Value};";
            }
            else
            {
                if (bFirst)
                    bFirst = false;
                else
                {
                    values.Append(", ");
                }

                values.Append($"{newColumnName}={Helpers.SpecialRuleForColumnValue(column.ColumnName)}");
            }
        }
        sql.Append(values.ToString());
        sql.Append(where);

        return sql.ToString();
    }
}