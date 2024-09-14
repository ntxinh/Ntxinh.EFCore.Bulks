using System.Data;
using Microsoft.Data.SqlClient;

namespace Ntxinh.EFCore.Bulks;

public static class SqlBulkCopyHelper
{
    public static async Task SqlBulkCopyAsync(DataTable data, string tableName, IEnumerable<ColumnMapDto> columnMappings, SqlConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            // Clone a new SqlConnection to fix:
            // - Exception: System.InvalidOperationException 'The ConnectionString property has not been initialized'
            // - 'AppDbContext' disposed.
            // - Disposing connection to database '' on server ''.
            // - Opening connection to database '' on server ''.
            var newSqlConn = new SqlConnection(connection.ConnectionString);

            using (newSqlConn)
            {
                await newSqlConn.OpenAsync(cancellationToken);
                using (var bulkCopy = new SqlBulkCopy(newSqlConn))
                {
                    bulkCopy.DestinationTableName = tableName;
                    bulkCopy.BulkCopyTimeout = 0; // Default 30
                    // bulkCopy.BatchSize = 0; // Default 0
                    foreach (var item in columnMappings)
                    {
                        // bulkCopy.ColumnMappings.Add("DataTableColumnName2", "DatabaseColumnName2");
                        bulkCopy.ColumnMappings.Add(item.EntityColumn.ColumnName, item.SqlColumn.ColumnName);
                    }

                    await bulkCopy.WriteToServerAsync(data, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            // Console.WriteLine(ex.Message);
            // return;
            throw ex;
        }
    }
}
