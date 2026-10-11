using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class BulkUpdateTests(SqlServerFixture fixture) : IAsyncLifetime
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

    private static async Task<List<Order>> OrdersAsync(TestDbContext db, CancellationToken ct) =>
        await db.Set<Order>().AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);

    [Fact]
    public async Task UpdatesOnlySetColumnsMatchedOnPrimaryKey()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(
            new[]
            {
                new Order { CustomerId = customer.Id, Name = "a", Total = 1m, Note = "n1" },
                new Order { CustomerId = customer.Id, Name = "b", Total = 2m, Note = "n2" },
            },
            cancellationToken: ct);
        var changes = await OrdersAsync(db, ct);
        foreach (var order in changes)
        {
            order.Total += 100m;
            order.Note = null;
            order.Name = "ignored";
        }

        var updated = await db.BulkUpdateAsync(changes, set: x => new { x.Total, x.Note }, cancellationToken: ct);

        Assert.Equal(2, updated);
        var saved = await OrdersAsync(db, ct);
        Assert.Equal(new[] { "a", "b" }, saved.Select(x => x.Name));
        Assert.Equal(new[] { 101m, 102m }, saved.Select(x => x.Total));
        Assert.All(saved, x => Assert.Null(x.Note));
    }

    [Fact]
    public async Task UpdatesRowsWithCompositeKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        await db.BulkInsertAsync(
            new[]
            {
                new OrderLine { OrderId = 1, LineNo = 1, Price = 10m, Qty = 1 },
                new OrderLine { OrderId = 1, LineNo = 2, Price = 20m, Qty = 1 },
                new OrderLine { OrderId = 2, LineNo = 1, Price = 30m, Qty = 1 },
            },
            cancellationToken: ct);

        var updated = await db.BulkUpdateAsync(new[] { new OrderLine { OrderId = 1, LineNo = 2, Price = 99m } }, set: x => x.Price, cancellationToken: ct);

        Assert.Equal(1, updated);
        var lines = await db.Set<OrderLine>().AsNoTracking().OrderBy(x => x.OrderId).ThenBy(x => x.LineNo).ToListAsync(ct);
        Assert.Equal(new[] { 10m, 99m, 30m }, lines.Select(x => x.Price));
        Assert.All(lines, x => Assert.Equal(1, x.Qty));
    }

    [Fact]
    public async Task MatchOnNonKeyColumnUpdatesEveryMatchingRow()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var a = await AddCustomerAsync(db, "a", ct);
        var b = await AddCustomerAsync(db, "b", ct);
        await db.BulkInsertAsync(
            new[]
            {
                new Order { CustomerId = a.Id, Name = "a1" },
                new Order { CustomerId = a.Id, Name = "a2" },
                new Order { CustomerId = a.Id, Name = "a3" },
                new Order { CustomerId = b.Id, Name = "b1" },
            },
            cancellationToken: ct);

        var updated = await db.BulkUpdateAsync(
            new[] { new Order { CustomerId = a.Id, Note = "bulk" } },
            set: x => x.Note!,
            matchOn: x => x.CustomerId,
            cancellationToken: ct);

        Assert.Equal(3, updated);
        var saved = await OrdersAsync(db, ct);
        Assert.Equal(new string?[] { "bulk", "bulk", "bulk", null }, saved.Select(x => x.Note));
    }

    [Fact]
    public async Task NullMatchOnValuesMatchNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(
            new[]
            {
                new Order { CustomerId = customer.Id, Name = "a", Note = "x", Total = 1m },
                new Order { CustomerId = customer.Id, Name = "b", Note = null, Total = 2m },
            },
            cancellationToken: ct);

        var updated = await db.BulkUpdateAsync(
            new[] { new Order { Note = "x", Total = 10m }, new Order { Note = null, Total = 20m } },
            set: x => x.Total,
            matchOn: x => x.Note!,
            cancellationToken: ct);

        Assert.Equal(1, updated);
        Assert.Equal(new[] { 10m, 2m }, (await OrdersAsync(db, ct)).Select(x => x.Total));
    }

    [Fact]
    public async Task DuplicateMatchOnValuesThrowAndChangeNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(new[] { new Order { CustomerId = customer.Id, Name = "a" } }, cancellationToken: ct);

        await Assert.ThrowsAsync<ArgumentException>(() => db.BulkUpdateAsync(
            new[] { new Order { CustomerId = customer.Id, Note = "first" }, new Order { CustomerId = customer.Id, Note = "second" } },
            set: x => x.Note!,
            matchOn: x => x.CustomerId,
            cancellationToken: ct));

        Assert.All(await OrdersAsync(db, ct), x => Assert.Null(x.Note));
    }

    [Fact]
    public async Task UpdateRollsBackWithCallerTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        var customer = await AddCustomerAsync(db, "c", ct);
        await db.BulkInsertAsync(new[] { new Order { CustomerId = customer.Id, Name = "a", Total = 1m } }, cancellationToken: ct);
        var order = Assert.Single(await OrdersAsync(db, ct));

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            order.Total = 50m;
            await db.BulkUpdateAsync(new[] { order }, set: x => x.Total, cancellationToken: ct);
            await transaction.RollbackAsync(ct);
        }

        Assert.Equal(1m, Assert.Single(await OrdersAsync(db, ct)).Total);
    }

    [Fact]
    public async Task RetryingContextCanUpdate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext(retryOnFailure: true);
        await db.BulkInsertAsync(new[] { new Customer { Name = "before" } }, cancellationToken: ct);
        var customer = await db.Set<Customer>().AsNoTracking().SingleAsync(ct);
        customer.Name = "after";

        var updated = await db.BulkUpdateAsync(new[] { customer }, set: x => x.Name, cancellationToken: ct);

        Assert.Equal(1, updated);
    }
}
