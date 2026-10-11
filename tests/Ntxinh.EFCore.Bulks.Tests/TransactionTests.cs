using System.Data;

using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class TransactionTests(SqlServerFixture fixture) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<int> CustomerCountAsync(CancellationToken ct)
    {
        await using var fresh = fixture.CreateContext();
        return await fresh.Set<Customer>().CountAsync(ct);
    }

    [Fact]
    public async Task CallerTransactionRollbackDiscardsBulkRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.BulkInsertAsync(new[] { new Customer { Name = "rolled back" } }, cancellationToken: ct);
            var visibleInside = await db.Set<Customer>().CountAsync(ct);
            Assert.Equal(1, visibleInside);
            await transaction.RollbackAsync(ct);
        }

        Assert.Equal(0, await CustomerCountAsync(ct));
    }

    [Fact]
    public async Task CallerTransactionCommitKeepsBulkRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.BulkInsertAsync(new[] { new Customer { Name = "kept" } }, cancellationToken: ct);
            await transaction.CommitAsync(ct);
        }

        Assert.Equal(1, await CustomerCountAsync(ct));
    }

    [Fact]
    public async Task RetryingExecutionStrategyIsHonoured()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext(retryOnFailure: true);

        var inserted = await db.BulkInsertAsync(new[] { new Customer { Name = "retry" } }, cancellationToken: ct);

        Assert.Equal(1, inserted);
    }

    // QXM calls bulk inserts from finally blocks after committing or rolling back, before disposing.
    [Fact]
    public async Task StartsOwnTransactionAfterCallerTransactionCompleted()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await transaction.RollbackAsync(ct);

        await db.BulkInsertAsync(new[] { new Customer { Name = "after" } }, cancellationToken: ct);

        Assert.Equal(1, await CustomerCountAsync(ct));
    }

    [Fact]
    public async Task LeavesACallerOpenedConnectionOpen()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();
        await db.Database.OpenConnectionAsync(ct);

        await db.BulkInsertAsync(new[] { new Customer { Name = "open" } }, cancellationToken: ct);

        Assert.Equal(ConnectionState.Open, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task LeavesAClosedConnectionClosed()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = fixture.CreateContext();

        await db.BulkInsertAsync(new[] { new Customer { Name = "closed" } }, cancellationToken: ct);

        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
