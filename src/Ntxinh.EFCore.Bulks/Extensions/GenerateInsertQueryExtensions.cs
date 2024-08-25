using System.Data;
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
        var sql = new StringBuilder("INSERT INTO " + tableName + " (");
        var values = new StringBuilder("VALUES (");
        bool bFirst = true;
        bool bIdentity = false;
        string identityType = string.Empty;

        foreach (DataColumn column in dataTable.Columns)
        {
            if (!column.AutoIncrement
                && primaryKeyColumnName is not null
                && !primaryKeyColumnName.Equals(default(KeyValuePair<string, string>))
                && column.ColumnName == primaryKeyColumnName.Value.Key)
            {
                column.AutoIncrement = true;
            }

            if (column.AutoIncrement)
            {
                bIdentity = true;

                switch (column.DataType.Name)
                {
                    case "Int16":
                        identityType = "smallint";
                        break;
                    case "SByte":
                        identityType = "tinyint";
                        break;
                    case "Int64":
                        identityType = "bigint";
                        break;
                    case "Decimal":
                        identityType = "decimal";
                        break;
                    default:
                        identityType = "int";
                        break;
                }
            }
            else
            {
                var newColumnName = column.ColumnName;
                var columnMapping = columnMappings.FirstOrDefault(x => x.Key == column.ColumnName);
                if (!columnMapping.Equals(default(KeyValuePair<string, string>)))
                {
                    newColumnName = columnMapping.Value;
                }
                if (string.IsNullOrEmpty(newColumnName)) continue;

                if (bFirst)
                    bFirst = false;
                else
                {
                    sql.Append(", ");
                    values.Append(", ");
                }

                sql.Append(newColumnName);
                values.Append(Helpers.SpecialRuleForColumnValue(column.ColumnName));
            }
        }
        sql.Append(") ");
        sql.Append(values.ToString());
        sql.Append(")");

        if (getReturnId && bIdentity)
        {
            sql.Append("; SELECT CAST(scope_identity() AS ");
            sql.Append(identityType);
            sql.Append(")");
        }

        return sql.ToString();
    }
}