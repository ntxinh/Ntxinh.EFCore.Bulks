using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class BulkInsertExtensions
{
    public static async Task BulkInsertAsync<T>
    (
        this DbContext dbContext,
        IEnumerable<T> data,
        SqlBulkCopyOptionsDto options = null,
        string connectionString = null,
        CancellationToken cancellationToken = default
    ) where T : class
    {
        if (data is null || !data.Any())
            return;

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
            string.IsNullOrWhiteSpace(tableName)
            // || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
        )
            return;

        // Try to extract ConnectionString from DbContext
        if (string.IsNullOrWhiteSpace(connectionString) && connection is not null)
        {
            var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
            if (!builder.PersistSecurityInfo)
                return;
            connectionString = connection.ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var dataTable = DataTableHelper.CreateDataTable<T>(data, exludesColumns);

        if (dataTable is null || dataTable.Rows.Count <= 0)
            return;

        await SqlBulkCopyHelper.SqlBulkCopyAsync(dataTable, tableName/* , primaryKeyColumnName */, columnMappings, options, connectionString, cancellationToken);
    }

    public static async Task BulkInsertAsync
    (
        this DbContext dbContext,
        Type clrEntityType,
        DataTable dataTable,
        SqlBulkCopyOptionsDto options = null,
        string connectionString = null,
        CancellationToken cancellationToken = default
    )
    {
        if (clrEntityType is null || dataTable is null || dataTable.Rows.Count <= 0)
            return;

        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(clrEntityType);

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        // var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrWhiteSpace(tableName)
            // || primaryKeyColumnName is null
            || columnMappings is null || !columnMappings.Any()
        )
            return;

        // Try to extract ConnectionString from DbContext
        if (string.IsNullOrWhiteSpace(connectionString) && connection is not null)
        {
            var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
            if (!builder.PersistSecurityInfo)
                return;
            connectionString = connection.ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await SqlBulkCopyHelper.SqlBulkCopyAsync(dataTable, tableName/* , primaryKeyColumnName */, columnMappings, options, connectionString, cancellationToken);
    }

    public static async Task BulkInsertMultipleTablesAsync(this DbContext dbContext, IEnumerable<BulkInsertMultipleTablesDto> inputs, SqlBulkCopyOptionsDto options = null, string connectionString = null, CancellationToken cancellationToken = default)
    {
        var tables = new List<SqlBulkCopyDto>();
        SqlConnection? connection = null;

        foreach (var input in inputs)
        {
            if (input.ClrEntityType is null || input.DataTable is null || input.DataTable.Rows.Count <= 0)
                continue;

            // Extract data
            var columnMappingsResult = dbContext.ExtractDbContext(input.ClrEntityType);
            if (columnMappingsResult is null)
                return;

            // Destruction data
            var tableName = columnMappingsResult.TableName;
            // var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
            var columnMappings = columnMappingsResult.ColumnMappings;

            // Get SqlConnection from DbContext
            if (connection is null && columnMappingsResult.Connection is not null)
            {
                connection = columnMappingsResult.Connection;
            }

            // Validate extract data
            if (
                string.IsNullOrWhiteSpace(tableName)
                // || primaryKeyColumnName is null
                || columnMappings is null || !columnMappings.Any()
            )
                continue;

            tables.Add(new SqlBulkCopyDto
            {
                TableName = tableName,
                Data = input.DataTable,
                ColumnMappings = columnMappings,
            });
        }

        if (!tables.Any())
            return;

        // Try to extract ConnectionString from DbContext
        if (string.IsNullOrWhiteSpace(connectionString) && connection is not null)
        {
            var builder = new SqlConnectionStringBuilder(connection.ConnectionString);
            if (!builder.PersistSecurityInfo)
                return;
            connectionString = connection.ConnectionString;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await SqlBulkCopyHelper.SqlBulkCopyAsync(tables, options, connectionString, cancellationToken);
    }
}
