using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>One mapped column: how to read its value from an entity and how to name it in SQL.</summary>
internal sealed record ColumnMap(
    string PropertyName,
    string ColumnName,
    Type ProviderType,
    bool IsIdentity,
    bool IsStoreGenerated,
    Func<object, object?> Read)
{
    public string Delimited => TableMap.Delimit(ColumnName);
}

/// <summary>What the EF model says about one entity's table, built once per model and CLR type.</summary>
internal sealed class TableMap
{
    private static readonly ConditionalWeakTable<IModel, ConcurrentDictionary<Type, TableMap>> Cache = new();

    private TableMap(Type clrType, string table, IReadOnlyList<ColumnMap> columns, IReadOnlyList<ColumnMap> primaryKey)
    {
        ClrType = clrType;
        Table = table;
        Columns = columns;
        PrimaryKey = primaryKey;
    }

    public Type ClrType { get; }

    /// <summary>Delimited table name, schema-qualified when the model sets a schema: <c>[sales].[Orders]</c>.</summary>
    public string Table { get; }

    public IReadOnlyList<ColumnMap> Columns { get; }

    public IReadOnlyList<ColumnMap> PrimaryKey { get; }

    public IReadOnlyList<ColumnMap> InsertColumns(bool keepIdentity) =>
        Columns.Where(c => !c.IsStoreGenerated && (keepIdentity || !c.IsIdentity)).ToList();

    public ColumnMap Column(string propertyName, string argumentName)
    {
        var column = Columns.FirstOrDefault(c => c.PropertyName == propertyName)
            ?? throw new ArgumentException($"'{propertyName}' is not a mapped column of {ClrType.Name}.", argumentName);
        return column.IsStoreGenerated
            ? throw new ArgumentException($"'{propertyName}' is computed by the database and cannot be written or matched.", argumentName)
            : column;
    }

    public static TableMap For(DbContext db, Type clrType)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clrType);
        return Cache.GetOrCreateValue(db.Model).GetOrAdd(clrType, static (type, model) => Build(model, type), db.Model);
    }

    // SQL Server delimiting rules. EF's ISqlGenerationHelper is not used because the InMemory provider lacks it
    // and GetInsertSql must work there (consumer unit tests).
    public static string Delimit(string name) => $"[{name.Replace("]", "]]", StringComparison.Ordinal)}]";

    private static TableMap Build(IModel model, Type clrType)
    {
        var entityType = model.FindEntityType(clrType)
            ?? throw new InvalidOperationException($"{clrType.Name} is not an entity type in this DbContext's model.");
        var name = entityType.GetTableName()
            ?? throw new InvalidOperationException($"{clrType.Name} is not mapped to a table.");
        var schema = entityType.GetSchema();

        if (entityType.BaseType is not null || entityType.GetDirectlyDerivedTypes().Any())
        {
            throw Unsupported(clrType, "it is part of an inheritance hierarchy");
        }

        if (entityType.GetNavigations().Any(n => n.TargetEntityType.IsOwned()))
        {
            throw Unsupported(clrType, "it has owned types");
        }

        if (entityType.GetComplexProperties().Any())
        {
            throw Unsupported(clrType, "it has complex properties");
        }

        if (model.GetEntityTypes().Count(e => e.GetTableName() == name && e.GetSchema() == schema) > 1)
        {
            throw Unsupported(clrType, $"another entity type is mapped to table {name}");
        }

        var store = StoreObjectIdentifier.Table(name, schema);
        var columns = entityType.GetProperties().Select(p => MapColumn(p, store, clrType)).ToList();
        IReadOnlyList<ColumnMap> primaryKey = entityType.FindPrimaryKey() is { } key
            ? key.Properties.Select(p => columns.Single(c => c.PropertyName == p.Name)).ToList()
            : [];
        var table = schema is null ? Delimit(name) : $"{Delimit(schema)}.{Delimit(name)}";

        return new TableMap(clrType, table, columns, primaryKey);
    }

    private static ColumnMap MapColumn(IProperty property, StoreObjectIdentifier store, Type clrType)
    {
        if (property.IsShadowProperty())
        {
            throw Unsupported(clrType, $"'{property.Name}' is a shadow property");
        }

        var member = (MemberInfo?)property.PropertyInfo ?? property.FieldInfo
            ?? throw Unsupported(clrType, $"'{property.Name}' has no readable CLR member");

        // Compiled once per column; avoids reflection per row.
        var entity = Expression.Parameter(typeof(object), "entity");
        var access = Expression.MakeMemberAccess(Expression.Convert(entity, member.DeclaringType!), member);
        var getter = Expression.Lambda<Func<object, object?>>(Expression.Convert(access, typeof(object)), entity).Compile();

        var converter = property.GetTypeMapping().Converter;
        Func<object, object?> read = converter is null ? getter : e => converter.ConvertToProvider(getter(e));
        var providerType = converter?.ProviderClrType ?? property.ClrType;

        return new ColumnMap(
            property.Name,
            property.GetColumnName(store) ?? throw Unsupported(clrType, $"'{property.Name}' has no column in table {store.Name}"),
            Nullable.GetUnderlyingType(providerType) ?? providerType,
            IsIdentity: property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn,
            IsStoreGenerated: property.GetComputedColumnSql() is not null
                || (property.IsConcurrencyToken && property.ValueGenerated == ValueGenerated.OnAddOrUpdate),
            Read: read);
    }

    private static NotSupportedException Unsupported(Type clrType, string reason) =>
        new($"Bulk operations do not support {clrType.Name}: {reason}.");
}
