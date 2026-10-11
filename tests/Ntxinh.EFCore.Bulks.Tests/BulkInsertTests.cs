using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class BulkInsertTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<Customer> AddCustomerAsync(TestDbContext db, string name, CancellationToken ct)
    {
        var customer = new Customer { Name = name };
        db.Add(customer);
        await db.SaveChangesAsync(ct);
        return customer;
    }

    [Fact]
    public async Task InsertsEveryWritableColumnFaithfully()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        var createdAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234567);
        Order[] orders =
        [
            new() { CustomerId = customer.Id, Name = "O'Brien – 日本", Status = OrderStatus.Placed, Total = 12.34m, CreatedAt = createdAt, Note = null },
            new() { CustomerId = customer.Id, Name = "plain", Status = OrderStatus.Draft, Total = 0.01m, CreatedAt = createdAt, Note = "it's fine" },
        ];

        var inserted = await db.BulkInsertAsync(orders, cancellationToken: ct);

        Assert.Equal(2, inserted);
        var saved = await db.Set<Order>().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        Assert.Collection(
            saved,
            first =>
            {
                Assert.Equal("O'Brien – 日本", first.Name);
                Assert.Equal(OrderStatus.Placed, first.Status);
                Assert.Equal(12.34m, first.Total);
                Assert.Equal(createdAt, first.CreatedAt);
                Assert.Null(first.Note);
                Assert.Equal(first.Name.Length, first.NameLength);
                Assert.NotEmpty(first.Version);
            },
            second =>
            {
                Assert.Equal("plain", second.Name);
                Assert.Equal("it's fine", second.Note);
            });
        var storedStatuses = await db.Database.SqlQueryRaw<string>("SELECT [Status] AS [Value] FROM [sales].[Orders]").ToListAsync(ct);
        Assert.Contains("Placed", storedStatuses);
    }

    [Fact]
    public async Task KeepIdentityWritesSuppliedIdsOtherwiseTheDatabaseAssignsThem()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await db.BulkInsertAsync(new[] { new Customer { Id = 1000, Name = "kept" } }, new BulkOptions { KeepIdentity = true }, ct);
        await db.BulkInsertAsync(new[] { new Customer { Id = 1000, Name = "assigned" } }, cancellationToken: ct);

        var ids = await db.Set<Customer>().AsNoTracking().ToDictionaryAsync(x => x.Name, x => x.Id, ct);
        Assert.Equal(1000, ids["kept"]);
        Assert.NotEqual(1000, ids["assigned"]);
    }

    [Fact]
    public async Task RejectsOrphanForeignKeyAndRollsBackEarlierBatches()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        Order[] orders =
        [
            new() { CustomerId = customer.Id, Name = "a" },
            new() { CustomerId = customer.Id, Name = "b" },
            new() { CustomerId = -1, Name = "orphan" },
        ];

        await Assert.ThrowsAsync<SqlException>(() => db.BulkInsertAsync(orders, new BulkOptions { BatchSize = 1 }, ct));

        var count = await db.Set<Order>().CountAsync(ct);
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task CheckConstraintsOffAllowsOrphanForeignKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        var inserted = await db.BulkInsertAsync(new[] { new Order { CustomerId = -1, Name = "orphan" } }, new BulkOptions { CheckConstraints = false }, ct);

        Assert.Equal(1, inserted);
    }

    [Fact]
    public async Task NonGenericOverloadInsertsObjectsOfTheEntityType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        IReadOnlyList<object> rows = [new Customer { Name = "a" }, new Customer { Name = "b" }];

        var inserted = await db.BulkInsertAsync(typeof(Customer), rows, cancellationToken: ct);

        Assert.Equal(2, inserted);
    }

    [Fact]
    public async Task WritesToTableWhoseNameContainsBracket()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        var inserted = await db.BulkInsertAsync(new[] { new AuditRow { Message = "x" } }, cancellationToken: ct);

        Assert.Equal(1, inserted);
        var count = await db.Set<AuditRow>().CountAsync(ct);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InsertSqlRunsWithNamedParameters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        var affected = await db.Database.ExecuteSqlRawAsync(db.GetInsertSql<Customer>(), new object[] { new SqlParameter("@Name", "templated") }, ct);

        Assert.Equal(1, affected);
        Assert.True(await db.Set<Customer>().AnyAsync(x => x.Name == "templated", ct));
    }
}
