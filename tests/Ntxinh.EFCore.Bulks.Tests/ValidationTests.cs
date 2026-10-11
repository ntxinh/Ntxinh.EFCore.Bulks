namespace Ntxinh.EFCore.Bulks.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task BulkInsertOfNothingReturnsZeroWithoutConnecting()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        Assert.Equal(0, await db.BulkInsertAsync(Array.Empty<Customer>(), cancellationToken: ct));
    }

    [Fact]
    public async Task BulkInsertRejectsTypesOutsideTheModel()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.BulkInsertAsync(typeof(string), new[] { "x" }, cancellationToken: ct));
    }

    [Fact]
    public async Task BulkInsertRejectsNullElements()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => db.BulkInsertAsync(typeof(Customer), new object?[] { new Customer(), null }, cancellationToken: ct));
        Assert.Contains("null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkInsertRejectsElementsOfAnotherType()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = TestDbContext.Create();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => db.BulkInsertAsync(typeof(Customer), new object[] { new Order() }, cancellationToken: ct));
        Assert.Contains(nameof(Order), error.Message, StringComparison.Ordinal);
    }
}
