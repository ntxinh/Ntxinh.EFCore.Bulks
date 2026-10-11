# Ntxinh.EFCore.Bulks

Transaction-aware bulk insert and bulk update for EF Core on SQL Server, built on `SqlBulkCopy` and your EF model.

- NuGet: https://www.nuget.org/packages/Ntxinh.EFCore.Bulks
- Targets `net8.0` (EF Core 8.0.14+) and `net10.0` (EF Core 10.0.0+)
- SQL Server only

```sh
dotnet add package Ntxinh.EFCore.Bulks
```

## Bulk insert

```cs
using Ntxinh.EFCore.Bulks;

var inserted = await db.BulkInsertAsync(orders);                     // rows inserted
await db.BulkInsertAsync(orders, new BulkOptions { BatchSize = 5_000, Timeout = TimeSpan.FromMinutes(2) });
await db.BulkInsertAsync(typeof(Order), rows);                       // type known only at run time
```

- Table, schema, column names and value converters come from the EF model.
- Identity columns are assigned by SQL Server unless `KeepIdentity = true`. Generated values are **not** written back to the entities.
- Computed and rowversion columns are skipped.
- CHECK and FOREIGN KEY constraints are enforced (`CheckConstraints = true` by default).

## Bulk update

```cs
// Match on the primary key, update two columns.
await db.BulkUpdateAsync(orders, set: x => new { x.Total, x.Note });

// Match on another column; every matching row is updated.
await db.BulkUpdateAsync(patches, set: x => x.Note, matchOn: x => x.CustomerId);
```

Rows are bulk-copied into a temporary table and applied with one `UPDATE … JOIN`; values never appear in SQL text.
`matchOn` values must be unique within the input (otherwise `ArgumentException`). Rows whose `matchOn` value is `NULL` match nothing.

## Transactions

- If the DbContext has a transaction open, the operation joins it; you commit or roll back.
- Otherwise each call runs in its own transaction: all rows or none (also across batches).
- Contexts configured with `EnableRetryOnFailure` are supported.
- The DbContext's own connection is used, so Managed Identity / token authentication works and no connection string is needed.

Several tables, all or nothing:

```cs
await using var transaction = await db.Database.BeginTransactionAsync();
await db.BulkInsertAsync(orders);
await db.BulkInsertAsync(lines);
await transaction.CommitAsync();
```

## SQL templates (Dapper)

```cs
var sql = db.GetInsertSql<Order>();
// INSERT INTO [sales].[Orders] ([CreatedAt], [CustomerId], ...) VALUES (@CreatedAt, @CustomerId, ...);
await connection.ExecuteAsync(sql, orders);

var stamped = db.GetInsertSql<Order>(new InsertSqlOptions { UtcNowColumns = ["CreatedAt", "UpdatedAt"] });
// CreatedAt is written as GETUTCDATE(); names an entity lacks are ignored.

var table = db.GetTableName<Order>();   // [sales].[Orders]
```

## Options

| `BulkOptions` | Default | Meaning |
|---|---|---|
| `BatchSize` | `0` | Rows per batch; `0` = one batch |
| `Timeout` | 30 s | Per SQL Server call; `TimeSpan.Zero` = no limit |
| `KeepIdentity` | `false` | Write the entities' identity values (insert only) |
| `CheckConstraints` | `true` | Enforce CHECK/FK constraints (insert only); `false` leaves FKs untrusted |

## Errors

| Situation | Exception |
|---|---|
| Entity not in the model or not mapped to a table | `InvalidOperationException` |
| Keyless entity updated without `matchOn` | `InvalidOperationException` |
| Owned or complex types, inheritance, table splitting, shadow properties | `NotSupportedException` |
| Database provider other than SQL Server | `NotSupportedException` |
| Bad selector, column in both `set` and `matchOn`, null or wrong-typed element, duplicate `matchOn` value | `ArgumentException` |
| Anything SQL Server rejects | `SqlException`, unchanged |

Nothing fails silently: there is no "skip and return" path.

## Upgrading from 8.x / 10.x

2.0 is a breaking rewrite. See the migration table in the [changelog](https://github.com/ntxinh/Ntxinh.EFCore.Bulks/blob/main/CHANGELOG.md).

## Contributing

```sh
dotnet build -c Release          # net8.0 + net10.0, warnings are errors
dotnet test                      # integration tests start SQL Server 2022 with Testcontainers (needs Docker)
```

With Podman instead of Docker:

```sh
env DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test
```

### Releasing (maintainers)

Releases are published by CI from a tag: `git tag v2.0.1 && git push origin v2.0.1`. Prerelease tags (`v2.1.0-rc.1`) publish prereleases.

One-time setup for NuGet Trusted Publishing (no long-lived API key):

1. nuget.org → your account → **Trusted Publishing** → add a policy: owner `ntxinh`, repository `Ntxinh.EFCore.Bulks`, workflow file `ci.yml`.
2. GitHub → repository → Settings → Secrets → Actions: add `NUGET_USER` = your nuget.org username.
