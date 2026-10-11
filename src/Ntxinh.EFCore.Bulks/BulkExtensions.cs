using System.Collections;
using System.Linq.Expressions;

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

    /// <summary>
    /// Updates the <paramref name="set"/> columns of existing rows from <paramref name="entities"/>, matching rows on
    /// <paramref name="matchOn"/> (default: the primary key), and returns the number of rows updated.
    /// </summary>
    /// <remarks>
    /// Rows are bulk-copied into a temporary table and applied with one <c>UPDATE … JOIN</c>; values never appear in SQL text.
    /// A <paramref name="matchOn"/> value may match several rows (all are updated); rows whose value is NULL match nothing.
    /// </remarks>
    /// <param name="db">The context whose model and connection are used.</param>
    /// <param name="entities">Rows carrying the match values and the new values.</param>
    /// <param name="set">Columns to update: <c>x =&gt; x.Price</c> or <c>x =&gt; new { x.Price, x.Note }</c>.</param>
    /// <param name="matchOn">Columns that identify the rows to update; default is the primary key.</param>
    /// <param name="options">Batch size and timeout; <see cref="BulkOptions.KeepIdentity"/> and <see cref="BulkOptions.CheckConstraints"/> do not apply.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ArgumentException">
    /// A selector is not a plain property selection, a column is in both <paramref name="set"/> and <paramref name="matchOn"/>,
    /// <paramref name="entities"/> contains a null element, or two entities share a <paramref name="matchOn"/> value.
    /// </exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table, or has no primary key and <paramref name="matchOn"/> is null.</exception>
    /// <exception cref="NotSupportedException">The mapping or the database provider is not supported.</exception>
    public static async Task<int> BulkUpdateAsync<T>(
        this DbContext db,
        IEnumerable<T> entities,
        Expression<Func<T, object>> set,
        Expression<Func<T, object>>? matchOn = null,
        BulkOptions? options = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(set);
        options ??= new BulkOptions();

        var map = TableMap.For(db, typeof(T));
        var setColumns = MemberSelector.Columns(set, map, nameof(set));
        var keyColumns = matchOn is not null
            ? MemberSelector.Columns(matchOn, map, nameof(matchOn))
            : map.PrimaryKey.Count > 0
                ? map.PrimaryKey
                : throw new InvalidOperationException($"{typeof(T).Name} has no primary key; pass matchOn.");
        if (setColumns.Intersect(keyColumns).Any())
        {
            throw new ArgumentException("A column cannot be in both set and matchOn.", nameof(set));
        }

        var rows = Materialize(entities, typeof(T));
        if (rows.Count == 0)
        {
            return 0;
        }

        var temp = $"#bulk_{Guid.NewGuid():N}";
        var columns = keyColumns.Concat(setColumns).ToList();
        var selectList = string.Join(", ", columns.Select(c => c.Delimited));
        var keyList = string.Join(", ", keyColumns.Select(c => c.Delimited));
        var joinOn = string.Join(" AND ", keyColumns.Select(c => $"t.{c.Delimited} = s.{c.Delimited}"));
        var assignments = string.Join(", ", setColumns.Select(c => $"t.{c.Delimited} = s.{c.Delimited}"));

        return await BulkExecutor.RunAsync(
            db,
            async (connection, transaction, ct) =>
            {
                // SELECT INTO copies exact column types and collations. Creating it is transactional,
                // so a rollback on any failure below also removes it.
                await using (var create = BulkExecutor.Command(connection, transaction, $"SELECT TOP 0 {selectList} INTO {temp} FROM {map.Table};", options))
                {
                    await create.ExecuteNonQueryAsync(ct);
                }

                await BulkExecutor.CopyAsync(connection, transaction, temp, rows, columns, SqlBulkCopyOptions.KeepIdentity, options, ct);

                // Checked in SQL so SQL Server's own equality and collation decide what is a duplicate.
                await using (var duplicates = BulkExecutor.Command(connection, transaction, $"SELECT TOP 1 1 FROM {temp} GROUP BY {keyList} HAVING COUNT(*) > 1;", options))
                {
                    if (await duplicates.ExecuteScalarAsync(ct) is not null)
                    {
                        throw new ArgumentException("matchOn values must be unique within entities; SQL Server would apply one of the duplicates at random.");
                    }
                }

                int updated;
                await using (var update = BulkExecutor.Command(connection, transaction, $"UPDATE t SET {assignments} FROM {map.Table} AS t INNER JOIN {temp} AS s ON {joinOn};", options))
                {
                    updated = await update.ExecuteNonQueryAsync(ct);
                }

                await using (var drop = BulkExecutor.Command(connection, transaction, $"DROP TABLE {temp};", options))
                {
                    await drop.ExecuteNonQueryAsync(ct);
                }

                return updated;
            },
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
