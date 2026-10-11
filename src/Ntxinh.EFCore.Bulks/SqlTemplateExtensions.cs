using Microsoft.EntityFrameworkCore;

using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks;

/// <summary>SQL text built from the EF Core model, for Dapper or raw ADO.NET.</summary>
public static class SqlTemplateExtensions
{
    /// <summary>Returns the delimited, schema-qualified table name of <typeparamref name="T"/>, e.g. <c>[sales].[Orders]</c>.</summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> uses a mapping bulk operations do not support.</exception>
    public static string GetTableName<T>(this DbContext db) where T : class =>
        TableMap.For(db, typeof(T)).Table;

    /// <summary>
    /// Returns <c>INSERT INTO [schema].[Table] ([Column], …) VALUES (@Property, …);</c> with one parameter per CLR
    /// property name, for executing once per entity (e.g. Dapper <c>ExecuteAsync(sql, entities)</c>).
    /// Identity, computed and rowversion columns are omitted.
    /// </summary>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> is not mapped to a table.</exception>
    /// <exception cref="NotSupportedException"><typeparamref name="T"/> uses a mapping bulk operations do not support.</exception>
    public static string GetInsertSql<T>(this DbContext db, InsertSqlOptions? options = null) where T : class
    {
        var map = TableMap.For(db, typeof(T));
        var utcNow = (options ?? new InsertSqlOptions()).UtcNowColumns;
        var columns = map.InsertColumns(keepIdentity: false);
        var names = string.Join(", ", columns.Select(c => c.Delimited));
        var values = string.Join(", ", columns.Select(c => utcNow.Contains(c.PropertyName) ? "GETUTCDATE()" : $"@{c.PropertyName}"));
        return $"INSERT INTO {map.Table} ({names}) VALUES ({values});";
    }
}
