using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Testcontainers.MsSql;

namespace Ntxinh.EFCore.Bulks.Tests;

/// <summary>One SQL Server 2022 container per test run; tests in the collection run one at a time.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString { get; private set; } = "";

    public TestDbContext CreateContext(bool retryOnFailure = false) => TestDbContext.Create(ConnectionString, retryOnFailure);

    public async ValueTask InitializeAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await container.StartAsync(ct);
        ConnectionString = new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "BulksTests" }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(ct);
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public async Task ResetAsync()
    {
        await using var db = CreateContext();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [OrderLine]; DELETE FROM [sales].[Orders]; DELETE FROM [Customer]; DELETE FROM [Audit]]Rows];",
            TestContext.Current.CancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
