using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateUpdateQueryExtensions
{
    public static (string, ColumnInfoDto) GenerateUpdateQuery<T>(this DbContext dbContext) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var invalidColumnMappings = columnMappingsResult.InvalidColumnMappings;
        // var connection = columnMappingsResult.Connection;

        var exludesColumns = invalidColumnMappings is not null && invalidColumnMappings.Any()
            ? invalidColumnMappings.Select(x => x.EntityColumn.ColumnName).ToArray()
            : null;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            // || connection is null
        ) return (string.Empty, null);

        var dataTable = DataTableHelper.CreateDataTable<T>(exludesColumns);

        // Build query string
        var sql = new StringBuilder();
        sql.AppendFormat("UPDATE {0}{1}SET ", tableName, Constants.NewLine);
        var values = new StringBuilder();
        // var where = string.Empty;
        bool bFirst = true;

        // foreach (DataColumn column in dataTable.Columns)
        foreach (var columnMapping in columnMappings)
        {
            if (!dataTable.Columns.Contains(columnMapping.EntityColumn.ColumnName)) continue;
            var column = dataTable.Columns[columnMapping.EntityColumn.ColumnName];

            if (!column.AutoIncrement
                && primaryKeyColumnName is not null
                && column.ColumnName == primaryKeyColumnName.EntityColumn.ColumnName)
            {
                column.AutoIncrement = true;
            }

            var newColumnName = columnMapping.SqlColumn.ColumnName;
            if (string.IsNullOrEmpty(newColumnName)) continue;

            if (column.AutoIncrement)
            {
                // where = $"{Constants.NewLine}WHERE [{primaryKeyColumnName.SqlColumn.ColumnName}]=@{primaryKeyColumnName.EntityColumn.ColumnName};";
            }
            else
            {
                if (bFirst)
                    bFirst = false;
                else
                {
                    values.Append(", ");
                }

                values.AppendFormat("[{0}]={1}", newColumnName, Helpers.SpecialRuleForColumnValue(column.ColumnName));
            }
        }
        sql.Append(values.ToString());
        // sql.Append(where);
        sql.Append(";");

        return (sql.ToString(), primaryKeyColumnName.SqlColumn);
    }
}
