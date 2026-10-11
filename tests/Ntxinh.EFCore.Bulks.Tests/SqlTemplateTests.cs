namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class SqlTemplateTests
{
    [Fact]
    public void GetTableNameIsSchemaQualifiedAndDelimited()
    {
        using var db = TestDbContext.Create();

        Assert.Equal("[sales].[Orders]", db.GetTableName<Order>());
        Assert.Equal("[Audit]]Rows]", db.GetTableName<AuditRow>());
    }

    [Fact]
    public void GetInsertSqlListsWritableColumnsWithPropertyNamedParameters()
    {
        using var db = TestDbContext.Create();

        Assert.Equal(
            "INSERT INTO [sales].[Orders] ([CreatedAt], [CustomerId], [Name], [note_text], [Status], [Total]) "
            + "VALUES (@CreatedAt, @CustomerId, @Name, @Note, @Status, @Total);",
            db.GetInsertSql<Order>());
    }

    [Fact]
    public void GetInsertSqlKeepsNonIdentityKeys()
    {
        using var db = TestDbContext.Create();

        Assert.Equal(
            "INSERT INTO [OrderLine] ([OrderId], [LineNo], [Price], [Qty]) VALUES (@OrderId, @LineNo, @Price, @Qty);",
            db.GetInsertSql<OrderLine>());
    }

    [Fact]
    public void GetInsertSqlWritesUtcNowForListedColumnsAndIgnoresUnknownNames()
    {
        using var db = TestDbContext.Create();

        var sql = db.GetInsertSql<Order>(new InsertSqlOptions { UtcNowColumns = ["CreatedAt", "UpdatedAt"] });

        Assert.Equal(
            "INSERT INTO [sales].[Orders] ([CreatedAt], [CustomerId], [Name], [note_text], [Status], [Total]) "
            + "VALUES (GETUTCDATE(), @CustomerId, @Name, @Note, @Status, @Total);",
            sql);
    }

    [Fact]
    public void TemplatesRejectUnsupportedTypes()
    {
        using var db = TestDbContext.Create();

        Assert.Throws<NotSupportedException>(() => db.GetInsertSql<Owner>());
        Assert.Throws<InvalidOperationException>(() => db.GetTableName<string>());
    }
}
