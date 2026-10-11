using System.Collections;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks;

/// <summary>SqlBulkCopy-based writes for EF Core on SQL Server, driven by the DbContext's model.</summary>
/// <remarks>
/// Every operation uses the DbContext's own connection. It joins <c>Database.CurrentTransaction</c> when there is one;
/// otherwise it runs in its own transaction, so either all rows are written or none.
/// </remarks>
public static class BulkExtensions
{
    /// <summary>Inserts <paramref name="entities"/> with SqlBulkCopy and returns the number of rows inserted.</summary>
    /// <remarks>Database-generated identity values are not written back to the entities.</remarks>
    /// <exception cref="ArgumentException"><paramref name="entities"/> contains a null element.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException">The mapping or the database provider is not supported.</exception>
    public static Task<int> BulkInsertAsync<T>(
        this DbContext db,
        IEnumerable<T> entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class
        => db.BulkInsertAsync(typeof(T), entities, options, cancellationToken);

    /// <summary>
    /// Inserts <paramref name="entities"/>, each an instance of <paramref name="entityType"/>, with SqlBulkCopy and
    /// returns the number of rows inserted. Use when the type is only known at run time (e.g. queued work).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="entities"/> contains a null element or an element of another type.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="entityType"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException">The mapping or the database provider is not supported.</exception>
    public static async Task<int> BulkInsertAsync(
        this DbContext db,
        Type entityType,
        IEnumerable entities,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entityType);
        ArgumentNullException.ThrowIfNull(entities);
        options ??= new BulkOptions();

        var map = TableMap.For(db, entityType);
        var rows = Materialize(entities, entityType);
        if (rows.Count == 0)
        {
            return 0;
        }

        var columns = map.InsertColumns(options.KeepIdentity);
        var flags = (options.CheckConstraints ? SqlBulkCopyOptions.CheckConstraints : SqlBulkCopyOptions.Default)
            | (options.KeepIdentity ? SqlBulkCopyOptions.KeepIdentity : SqlBulkCopyOptions.Default);

        return await BulkExecutor.RunAsync(
            db,
            (connection, transaction, ct) => BulkExecutor.CopyAsync(connection, transaction, map.Table, rows, columns, flags, options, ct),
            cancellationToken);
    }

    // One pass before any connection is used: a bad element must fail fast and write nothing.
    private static ICollection Materialize(IEnumerable entities, Type entityType)
    {
        var rows = entities as ICollection ?? entities.Cast<object>().ToList();
        foreach (var row in rows)
        {
            if (row is null)
            {
                throw new ArgumentException("The entities collection contains a null element.", nameof(entities));
            }

            if (!entityType.IsInstanceOfType(row))
            {
                throw new ArgumentException($"The entities collection contains a {row.GetType().Name}; expected {entityType.Name}.", nameof(entities));
            }
        }

        return rows;
    }
}
