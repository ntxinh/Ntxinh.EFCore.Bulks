using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class BulkInsertExtensions
{
    public static async Task BulkInsertAsync<T>(this DbContext dbContext, IEnumerable<T> data, CancellationToken cancellationToken = default) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        // var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var invalidColumnMappings = columnMappingsResult.InvalidColumnMappings;
        var connection = columnMappingsResult.Connection;

        var exludesColumns = invalidColumnMappings is not null && invalidColumnMappings.Any()
            ? invalidColumnMappings.Select(x => x.EntityColumn.ColumnName).ToArray()
            : null;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            // || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            || connection is null
        )
            return;

        var dataTable = DataTableHelper.CreateDataTable<T>(data, exludesColumns);

        await SqlBulkCopyHelper.SqlBulkCopyAsync(dataTable, tableName/* , primaryKeyColumnName */, columnMappings, connection, cancellationToken);
    }

    public static async Task BulkInsertAsync(this DbContext dbContext, Type clrEntityType, DataTable dataTable, CancellationToken cancellationToken = default)
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(clrEntityType);

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        // var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            // || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
            || connection is null
        )
            return;

        await SqlBulkCopyHelper.SqlBulkCopyAsync(dataTable, tableName/* , primaryKeyColumnName */, columnMappings, connection, cancellationToken);
    }

    public static async Task BulkInsertMultipleTablesAsync(this DbContext dbContext, IEnumerable<BulkInsertMultipleTablesDto> inputs, SqlBulkCopyOptionsDto options, CancellationToken cancellationToken = default)
    {
        var tables = new List<SqlBulkCopyDto>();
        SqlConnection? connection = null;

        foreach (var input in inputs)
        {
            // Extract data
            var columnMappingsResult = dbContext.ExtractDbContext(input.ClrEntityType);
            if (columnMappingsResult is null)
                return;

            // Destruction data
            var tableName = columnMappingsResult.TableName;
            // var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
            var columnMappings = columnMappingsResult.ColumnMappings;
            if (connection is null && columnMappingsResult.Connection is not null)
            {
                connection = columnMappingsResult.Connection;
            }

            // Validate extract data
            if (
                string.IsNullOrEmpty(tableName)
                // || primaryKeyColumnName is null
                || columnMappings is null || !columnMappings.Any()
                || connection is null
            )
                continue;

            tables.Add(new SqlBulkCopyDto
            {
                TableName = tableName,
                Data = input.DataTable,
                ColumnMappings = columnMappings,
            });
        }

        if (!tables.Any() || connection is null)
            return;

        await SqlBulkCopyHelper.SqlBulkCopyAsync(tables, options, connection, cancellationToken);
    }
}
