using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateInsertQueryExtensions
{
    public static string GenerateInsertQuery<T>(this DbContext dbContext, bool getReturnId = false) where T : class
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
        ) return string.Empty;

        var dataTable = DataTableHelper.CreateDataTable<T>(exludesColumns);

        // Build query string
        var sql = new StringBuilder();
        sql.AppendFormat("INSERT INTO {0} (", tableName);
        var values = new StringBuilder("VALUES (");
        bool bFirst = true;
        bool bIdentity = false;
        string identityType = string.Empty;

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

            if (column.AutoIncrement)
            {
                bIdentity = true;

                identityType = columnMapping.SqlColumn.DataType;
            }
            else
            {
                var newColumnName = columnMapping.SqlColumn.ColumnName;
                if (string.IsNullOrEmpty(newColumnName)) continue;

                if (bFirst)
                    bFirst = false;
                else
                {
                    sql.Append(", ");
                    values.Append(", ");
                }

                sql.AppendFormat("[{0}]", newColumnName);
                values.Append(Helpers.SpecialRuleForColumnValue(column.ColumnName));
            }
        }
        sql.AppendFormat("){0}{1})", Constants.NewLine, values.ToString());

        if (getReturnId && bIdentity)
        {
            sql.AppendFormat("; SELECT CAST(scope_identity() AS {0})", identityType);
        }

        sql.Append(";");

        return sql.ToString();
    }
}
