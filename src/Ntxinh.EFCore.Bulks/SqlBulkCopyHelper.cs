using System.Data;
using Microsoft.Data.SqlClient;

namespace Ntxinh.EFCore.Bulks;

public static class SqlBulkCopyHelper
{
    public static async Task SqlBulkCopyAsync(DataTable data, string tableName, KeyValuePair<string, string>? primaryKeyColumnName, IDictionary<string, string> columnMappings, SqlConnection connection, CancellationToken cancellationToken = default)
    {
        /* if (primaryKeyColumnName is not null
            && !primaryKeyColumnName.Equals(default(KeyValuePair<string, string>)))
        {
            data.PrimaryKey = [data.Columns[primaryKeyColumnName.Value.Key]];
            data.Columns[primaryKeyColumnName.Value.Key].AutoIncrement = true;
        } */

        try
        {
            using (connection)
            {
                await connection.OpenAsync(cancellationToken);
                using (var bulkCopy = new SqlBulkCopy(connection))
                {
                    bulkCopy.DestinationTableName = tableName;
                    bulkCopy.BulkCopyTimeout = 0; // Default 30
                    // bulkCopy.BatchSize = 0; // Default 0
                    foreach (var item in columnMappings)
                    {
                        // bulkCopy.ColumnMappings.Add("DataTableColumnName2", "DatabaseColumnName2");
                        bulkCopy.ColumnMappings.Add(item.Key, item.Value);
                    }

                    await bulkCopy.WriteToServerAsync(data, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return;
        }
    }
}
