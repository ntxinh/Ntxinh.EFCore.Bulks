using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateMergeUpsertQueryExtensions
{
    public static (string, IEnumerable<SqlParameter>, ColumnInfoDto) GenerateMergeUpsertQuery<T>(this DbContext dbContext) where T : class
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
            string.IsNullOrWhiteSpace(tableName)
            || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            // || connection is null
        ) return (string.Empty, null, null);

        var dataTable = DataTableHelper.CreateDataTable<T>(exludesColumns);

        // Build query string

        var otherSelectColumns = string.Empty;
        var otherSetColumns = string.Empty;

        var otherInsertColumns = string.Empty;
        var otherValueColumns = string.Empty;

        var parameters = new List<SqlParameter>();

        foreach (var columnMapping in columnMappings)
        {
            if (!dataTable.Columns.Contains(columnMapping.EntityColumn.ColumnName))
                continue;
            var column = dataTable.Columns[columnMapping.EntityColumn.ColumnName];

            if (!column.AutoIncrement
                && primaryKeyColumnName is not null
                && column.ColumnName == primaryKeyColumnName.EntityColumn.ColumnName)
            {
                column.AutoIncrement = true;
            }

            if (column.AutoIncrement)
                continue;

            var sqlColumnName = columnMapping.SqlColumn.ColumnName;
            if (string.IsNullOrWhiteSpace(sqlColumnName))
                continue;

            var entityColumn = columnMapping.EntityColumn;
            var entityColumnName = entityColumn.ColumnName; // column.ColumnName;
            if (string.IsNullOrWhiteSpace(sqlColumnName))
                continue;

            var isDateColumn = Helpers.IsDateColumn(sqlColumnName);

            otherSelectColumns += isDateColumn
                ? string.Empty
                : $"@{entityColumnName} AS {sqlColumnName}, ";
            otherSetColumns += isDateColumn
                ? $"{sqlColumnName} = GETUTCDATE(), "
                : $"{sqlColumnName} = source.{sqlColumnName}, ";

            otherInsertColumns += $"{sqlColumnName}, ";
            otherValueColumns += isDateColumn
                ? "GETUTCDATE(), "
                : $"source.{sqlColumnName}, ";

            if (!isDateColumn)
            {
                parameters.Add(new SqlParameter($"@{entityColumnName}", TypeDefaultValue.GetDefaultValue(entityColumn.DataType)));
            }
        }

        // Remove comma at the end
        var minLength = 2;
        otherSelectColumns = otherSelectColumns.Length >= minLength ? otherSelectColumns.TrimEnd(',', ' ') : otherSelectColumns;
        otherSetColumns = otherSetColumns.Length >= minLength ? otherSetColumns.TrimEnd(',', ' ') : otherSetColumns;
        otherInsertColumns = otherInsertColumns.Length >= minLength ? otherInsertColumns.TrimEnd(',', ' ') : otherInsertColumns;
        otherValueColumns = otherValueColumns.Length >= minLength ? otherValueColumns.TrimEnd(',', ' ') : otherValueColumns;

        var sql =
        $"""
            MERGE INTO {tableName} AS target
            USING (SELECT @{primaryKeyColumnName.EntityColumn.ColumnName} AS {primaryKeyColumnName.SqlColumn.ColumnName}, {otherSelectColumns}) AS source
            ON target.{primaryKeyColumnName.SqlColumn.ColumnName} = source.{primaryKeyColumnName.SqlColumn.ColumnName}
            WHEN MATCHED THEN
                UPDATE SET
                    {otherSetColumns}
            WHEN NOT MATCHED THEN
                INSERT ({otherInsertColumns})
                VALUES ({otherValueColumns});
        """;

        return (sql, parameters, primaryKeyColumnName.SqlColumn);
    }
}
