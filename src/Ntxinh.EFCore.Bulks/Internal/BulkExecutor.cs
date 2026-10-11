using System.Collections;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>Runs bulk work on the DbContext's own SqlConnection, inside the caller's transaction or an owned one.</summary>
internal static class BulkExecutor
{
    public static async Task<int> RunAsync(
        DbContext db,
        Func<SqlConnection, SqlTransaction, CancellationToken, Task<int>> work,
        CancellationToken cancellationToken)
    {
        if (db.Database.GetDbConnection() is not SqlConnection connection)
        {
            throw new NotSupportedException($"Bulk operations require the SQL Server provider; this DbContext uses {db.Database.ProviderName}.");
        }

        // EF clears CurrentTransaction on commit/rollback, so a finished-but-undisposed caller transaction is not joined.
        if (db.Database.CurrentTransaction is { } callerTransaction)
        {
            return await work(connection, (SqlTransaction)callerTransaction.GetDbTransaction(), cancellationToken);
        }

        // Retrying strategies (EnableRetryOnFailure) reject transactions opened outside the strategy.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async ct =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var result = await work(connection, (SqlTransaction)transaction.GetDbTransaction(), ct);
                await transaction.CommitAsync(ct);
                return result;
            },
            cancellationToken);
    }

    public static async Task<int> CopyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string destination,
        IEnumerable rows,
        IReadOnlyList<ColumnMap> columns,
        SqlBulkCopyOptions flags,
        BulkOptions options,
        CancellationToken cancellationToken)
    {
        using var bulkCopy = new SqlBulkCopy(connection, flags, transaction)
        {
            DestinationTableName = destination,
            BatchSize = options.BatchSize,
            BulkCopyTimeout = (int)options.Timeout.TotalSeconds,
        };
        for (var i = 0; i < columns.Count; i++)
        {
            bulkCopy.ColumnMappings.Add(i, columns[i].ColumnName);
        }

        await using var reader = new EntityDataReader(rows, columns);
        await bulkCopy.WriteToServerAsync(reader, cancellationToken);
        return reader.RowCount;
    }

    public static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql, BulkOptions options)
    {
#pragma warning disable CA2100 // SQL holds only identifiers delimited by TableMap and generated temp names; values never appear in it.
        return new SqlCommand(sql, connection, transaction) { CommandTimeout = (int)options.Timeout.TotalSeconds };
#pragma warning restore CA2100
    }
}
