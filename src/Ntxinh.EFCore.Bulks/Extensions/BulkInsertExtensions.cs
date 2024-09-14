using System.Data;

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
        ) return;

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
        ) return;

        await SqlBulkCopyHelper.SqlBulkCopyAsync(dataTable, tableName/* , primaryKeyColumnName */, columnMappings, connection, cancellationToken);
    }
}
