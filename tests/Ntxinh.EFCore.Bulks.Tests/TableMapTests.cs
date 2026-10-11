using Ntxinh.EFCore.Bulks.Internal;

namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class TableMapTests
{
    private static TableMap Map<T>()
    {
        using var db = TestDbContext.Create();
        return TableMap.For(db, typeof(T));
    }

    private static string[] Names(IEnumerable<ColumnMap> columns) => columns.Select(c => c.PropertyName).ToArray();

    [Fact]
    public void ResolvesSchemaQualifiedDelimitedTableName()
    {
        Assert.Equal("[sales].[Orders]", Map<Order>().Table);
        Assert.Equal("[Customer]", Map<Customer>().Table);
    }

    [Fact]
    public void EscapesClosingBracketInNames()
    {
        Assert.Equal("[Audit]]Rows]", Map<AuditRow>().Table);
        Assert.Equal("[a]]b]", TableMap.Delimit("a]b"));
    }

    [Fact]
    public void MapsRenamedColumnsAndFlagsDatabaseGeneratedOnes()
    {
        var map = Map<Order>();

        Assert.Equal("note_text", map.Column("Note", "set").ColumnName);
        Assert.Equal("[note_text]", map.Column("Note", "set").Delimited);
        Assert.True(map.Columns.Single(c => c.PropertyName == "Id").IsIdentity);
        Assert.True(map.Columns.Single(c => c.PropertyName == "NameLength").IsStoreGenerated);
        Assert.True(map.Columns.Single(c => c.PropertyName == "Version").IsStoreGenerated);
        Assert.False(map.Columns.Single(c => c.PropertyName == "Total").IsStoreGenerated);
    }

    [Fact]
    public void InsertColumnsSkipIdentityUnlessKept()
    {
        var map = Map<Order>();

        Assert.Equal(new[] { "CreatedAt", "CustomerId", "Name", "Note", "Status", "Total" }, Names(map.InsertColumns(keepIdentity: false)));
        Assert.Equal(new[] { "Id", "CreatedAt", "CustomerId", "Name", "Note", "Status", "Total" }, Names(map.InsertColumns(keepIdentity: true)));
    }

    [Fact]
    public void ReadAppliesValueConvertersAndKeepsNulls()
    {
        var map = Map<Order>();
        var order = new Order { Status = OrderStatus.Placed, Total = 1.5m, Note = null };

        Assert.Equal("Placed", map.Column("Status", "set").Read(order));
        Assert.Equal(typeof(string), map.Column("Status", "set").ProviderType);
        Assert.Equal(1.5m, map.Column("Total", "set").Read(order));
        Assert.Null(map.Column("Note", "set").Read(order));
    }

    [Fact]
    public void PrimaryKeyFollowsKeyOrderAndIsEmptyForKeylessTypes()
    {
        Assert.Equal(new[] { "OrderId", "LineNo" }, Names(Map<OrderLine>().PrimaryKey));
        Assert.Empty(Map<AuditRow>().PrimaryKey);
    }

    [Fact]
    public void CachesOneMapPerModelAndType()
    {
        Assert.Same(Map<Order>(), Map<Order>());
    }

    [Theory]
    [InlineData(typeof(Owner))]
    [InlineData(typeof(Animal))]
    [InlineData(typeof(Dog))]
    [InlineData(typeof(Secretive))]
    public void RejectsUnsupportedMappings(Type type)
    {
        using var db = TestDbContext.Create();

        var error = Assert.Throws<NotSupportedException>(() => TableMap.For(db, type));
        Assert.Contains(type.Name, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTypesOutsideTheModel()
    {
        using var db = TestDbContext.Create();

        Assert.Throws<InvalidOperationException>(() => TableMap.For(db, typeof(string)));
    }

    [Fact]
    public void ColumnRejectsUnmappedAndDatabaseGeneratedProperties()
    {
        var map = Map<Order>();

        Assert.Equal("set", Assert.Throws<ArgumentException>(() => map.Column("Display", "set")).ParamName);
        Assert.Equal("matchOn", Assert.Throws<ArgumentException>(() => map.Column("NameLength", "matchOn")).ParamName);
    }
}
