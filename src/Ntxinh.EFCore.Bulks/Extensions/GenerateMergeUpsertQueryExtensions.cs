using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateMergeUpsertQueryExtensions
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
            string.IsNullOrWhiteSpace(tableName)
            || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            // || connection is null
        ) return (string.Empty, null);

        var dataTable = DataTableHelper.CreateDataTable<T>(exludesColumns);

        // Build query string

        var otherSelectColumns = string.Empty;
        var otherSetColumns = string.Empty;

        var otherInsertColumns = string.Empty;
        var otherValueColumns = string.Empty;

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

            var entityColumnName = column.ColumnName; // columnMapping.EntityColumn.ColumnName;
            if (string.IsNullOrWhiteSpace(sqlColumnName))
                continue;

            otherSelectColumns += Helpers.IsDateColumn(sqlColumnName)
                ? string.Empty
                : $"@{entityColumnName} AS {sqlColumnName}, ";
            otherSetColumns += Helpers.IsDateColumn(sqlColumnName)
                ? $"{sqlColumnName} = GETUTCDATE(), "
                : $"{sqlColumnName} = source.{sqlColumnName}, ";

            otherInsertColumns += $"{sqlColumnName}, ";
            otherValueColumns += Helpers.IsDateColumn(sqlColumnName)
                ? "GETUTCDATE(), "
                : $"source.{sqlColumnName}, ";
        }

        // Remove comma at the end
        otherSelectColumns = otherSelectColumns.Length >= 2 ? otherSelectColumns.TrimEnd(',', ' ') : otherSelectColumns;
        otherSetColumns = otherSetColumns.Length >= 2 ? otherSetColumns.TrimEnd(',', ' ') : otherSetColumns;
        otherInsertColumns = otherInsertColumns.Length >= 2 ? otherInsertColumns.TrimEnd(',', ' ') : otherInsertColumns;
        otherValueColumns = otherValueColumns.Length >= 2 ? otherValueColumns.TrimEnd(',', ' ') : otherValueColumns;

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

        return (sql, primaryKeyColumnName.SqlColumn);
    }
}
