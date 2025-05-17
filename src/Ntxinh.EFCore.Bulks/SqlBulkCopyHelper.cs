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

    public static async Task SqlBulkCopyAsync(IEnumerable<SqlBulkCopyDto> tables, SqlBulkCopyOptionsDto options, SqlConnection connection, CancellationToken cancellationToken = default)
    {
        try
        {
            if (tables is null || !tables.Any())
                return;

            var ops = (options?.KeepIdentity ?? false) ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default;

            await using SqlConnection newSqlConn = new SqlConnection(connection.ConnectionString);

            await newSqlConn.OpenAsync(cancellationToken);

            // await using SqlTransaction transaction = await newSqlConn.BeginTransactionAsync(cancellationToken);

            using (SqlTransaction transaction = newSqlConn.BeginTransaction())
            {
                try
                {
                    foreach (var table in tables)
                    {
                        using (var bulkCopy = new SqlBulkCopy(newSqlConn, ops, transaction))
                        {
                            bulkCopy.DestinationTableName = table.TableName;
                            bulkCopy.BulkCopyTimeout = options?.Timeout ?? 30;
                            bulkCopy.BatchSize = options?.BatchSize ?? 0;
                            foreach (var item in table.ColumnMappings)
                            {
                                bulkCopy.ColumnMappings.Add(item.EntityColumn.ColumnName, item.SqlColumn.ColumnName);
                            }

                            await bulkCopy.WriteToServerAsync(table.Data, cancellationToken);
                        }
                    }

                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    Console.WriteLine("Bulk insert failed: " + ex.Message);
                    throw ex;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Bulk insert failed: " + ex.Message);
            throw ex;
        }
    }
}
