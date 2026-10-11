# Ntxinh.EFCore.Bulks 2.0 — production-ready rewrite

- **Date:** 2026-10-11
- **Status:** Approved design, pending implementation plan
- **Consumer:** `azure_co4_backend` (QXM), currently on 8.0.14, net8.0 / EF Core 8.0.14

## 1. Goal

A safe, in-house SQL Server bulk library the QXM backend can rely on in production. Correctness first; small surface; no feature race with EFCore.BulkExtensions.

### Success criteria

1. QXM backend runs on 2.0.0 with identical data outcomes (totals/pricing rows match before vs after on QA).
2. Every failure path throws; no silent no-ops.
3. Integration tests against real SQL Server prove insert and update, on both net8.0 and net10.0.

### Decisions

| Topic | Decision |
|---|---|
| Scope | Correctness hardening, SQL Server only |
| Target frameworks | `net8.0` (EF Core 8.0.x) and `net10.0` (EF Core 10.0.x) |
| Compatibility | Clean break, version **2.0.0** (SemVer; drops the "tracks .NET major" scheme). Unused APIs deleted. Consumer migrated in the same effort |
| Engine | Own streaming engine over EF metadata, DbContext connection/transaction. No new runtime dependencies |
| Tests | xUnit v3 + Testcontainers.MsSql (SQL Server 2022), both TFMs |

### Defects being fixed (current 10.0.1 / 8.0.14)

1. Silent no-ops: unmapped entity, `PersistSecurityInfo=false` connection string, or missing connection string → `BulkInsertAsync` returns without writing or throwing.
2. `GenerateBulkUpdateQuery` interpolates values into SQL: injection, `'` breaks the query, null → `''`, culture-dependent `DateTime`.
3. Schema ignored and identifiers unquoted → non-`dbo` tables hit the wrong table.
4. `GenerateUpdateQuery` has no `WHERE`; `GenerateDeleteQuery` deletes the whole table.
5. Hidden convention: `CreatedAt/UpdatedAt/CreatedOn/UpdatedOn` silently become `GETUTCDATE()`.
6. Opens its own connection → cannot join the caller's transaction; incompatible with execution strategies.
7. Reflection → `DataTable`: ignores value converters, computed columns, owned types; doubles memory.
8. `throw ex` (stack trace lost), `Console.WriteLine` diagnostics, ~93 nullable warnings, no tests.

## 2. Public API

Namespace `Ntxinh.EFCore.Bulks`. All members are `DbContext` extension methods.

```csharp
// Writes — return rows affected
Task<int> BulkInsertAsync<T>(this DbContext db, IEnumerable<T> entities,
    BulkOptions? options = null, CancellationToken ct = default) where T : class;

Task<int> BulkInsertAsync(this DbContext db, Type entityType, IEnumerable entities,
    BulkOptions? options = null, CancellationToken ct = default);

Task<int> BulkUpdateAsync<T>(this DbContext db, IEnumerable<T> entities,
    Expression<Func<T, object>> set,              // x => new { x.DistributedPrice, x.BidLevelCost }  or  x => x.Price
    Expression<Func<T, object>>? matchOn = null,  // default: primary key
    BulkOptions? options = null, CancellationToken ct = default) where T : class;

// SQL templates (for Dapper)
string GetInsertSql<T>(this DbContext db, InsertSqlOptions? options = null) where T : class;
string GetTableName<T>(this DbContext db) where T : class;   // "[dbo].[OrderBillingCode]"

public sealed record BulkOptions
{
    public int BatchSize { get; init; }                              // 0 = single batch
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool KeepIdentity { get; init; }
    public bool CheckConstraints { get; init; } = true;               // behaviour change, see §4
}

public sealed record InsertSqlOptions
{
    public IReadOnlyCollection<string> UtcNowColumns { get; init; } = []; // CLR property names emitted as GETUTCDATE()
}
```

### Behaviour rules

- Empty input → returns `0`, no round-trip.
- Entity type not in the model, or an unsupported mapping (§3.1) → `InvalidOperationException` / `NotSupportedException` with the entity and property named.
- Non-SQL Server provider → `NotSupportedException`.
- `set` / `matchOn` must be a member access or an anonymous-object of member accesses on `T`, each mapped to a column; otherwise `ArgumentException`.
- `matchOn` columns must not appear in `set` → `ArgumentException`.
- Duplicate `matchOn` values in the input → `ArgumentException` (SQL Server `UPDATE…FROM` would pick a row nondeterministically).
- `SqlException` propagates unchanged.
- Transaction: joins `Database.CurrentTransaction` if present; otherwise the operation runs in its own transaction (all-or-nothing).

### `GetInsertSql<T>` output

`INSERT INTO [schema].[Table] ([ColA], [ColB]) VALUES (@PropA, @PropB);`

- Parameter names are CLR property names (Dapper binds entity properties by name).
- Identity and computed columns are omitted.
- Properties listed in `UtcNowColumns` emit `GETUTCDATE()` instead of a parameter.
- No `scope_identity()` option (unused by the consumer).

### Deleted public API

| Removed | Replacement |
|---|---|
| `BulkInsertAsync(Type, DataTable, …, connectionString)` | `BulkInsertAsync(Type, IEnumerable, …)` |
| `connectionString` parameter on all methods | DbContext connection |
| `BulkInsertMultipleTablesAsync`, `BulkInsertMultipleTablesDto` | Caller transaction + one `BulkInsertAsync` per table |
| `SqlBulkCopyOptionsDto` | `BulkOptions` |
| `GenerateInsertQuery<T>` | `GetInsertSql<T>` (+ `UtcNowColumns` for old date behaviour) |
| `GenerateBulkUpdateQuery<T>` | `BulkUpdateAsync<T>` |
| `GenerateDeleteQuery<T>` | `$"DELETE FROM {db.GetTableName<T>()}"` or EF `ExecuteDeleteAsync` |
| `GenerateUpdateQuery`, `GenerateSelectQuery`, `GenerateMergeUpsertQuery`, `GenerateCreateTableQuery`, `GenerateDropTableQuery`, `GenerateTruncateTableQuery` | None (unused) |
| `DataTableHelper`, `SqlBulkCopyHelper`, `DbContextExtensions.ExtractDbContext`, `ObjectExtensions`, `TypeDefaultValue`, `Helpers`, `Constants`, `ColumnInfoDto`, `ColumnMapDto`, `SqlDto`, `SqlBulkCopyDto` | None (internal concerns) |

## 3. Engine internals

Four internal units, one file each.

### 3.1 `TableMap` — metadata

Built once per `(IModel, Type)` and cached (`ConcurrentDictionary` keyed on the model instance).

- Source: `db.Model.FindEntityType(type)`; `StoreObjectIdentifier.Table(GetTableName(), GetSchema())`.
- Identifiers are delimited with SQL Server rules (`[name]`, `]` doubled) by one internal helper, `TableMap.Delimit`. EF's `ISqlGenerationHelper` is not used: the InMemory provider (used by the consumer's unit tests) does not register it, and `GetInsertSql` must work there. *(Amended 2026-10-11 after probing EF 8/10.)*
- Per column: column name (`GetColumnName(storeObject)`), CLR property name, EF compiled getter (`IProperty.GetGetter()`), provider converter (`GetTypeMapping().Converter?.ConvertToProvider`), flags `IsIdentity`, `IsComputed`, `IsKey`.
- Identity = `GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn`. Computed = `GetComputedColumnSql() is not null`.
- Insert column set: all columns except computed; identity excluded unless `KeepIdentity`.
- Unsupported → `NotSupportedException` naming the entity: owned or complex types, TPH discriminator, table splitting (more than one entity type on the table), shadow properties without a readable getter.

### 3.2 `EntityDataReader` — row streaming

Minimal `DbDataReader` over `IEnumerable<object>` and a `TableMap` column list. Implements `FieldCount`, `Read`, `GetValue`, `GetOrdinal`, `GetName`, `IsDBNull`, `GetFieldType`; everything else throws `NotSupportedException`. `GetValue` = getter → converter → `null` mapped to `DBNull.Value`. Counts rows read (insert return value). Inputs are validated before any connection is used: `null` elements and elements that are not the entity type → `ArgumentException`.

### 3.3 `BulkExecutor` — connection and transaction

1. `db.Database.GetDbConnection() as SqlConnection` (else `NotSupportedException`).
2. Connection lifetime is left to EF: `BeginTransactionAsync` opens the connection when needed and disposing the transaction closes it only if EF opened it, so a caller-opened connection stays open. Reusing EF's connection object removes the `PersistSecurityInfo` problem and supports token / Managed Identity auth.
3. If `Database.CurrentTransaction` exists: use its `GetDbTransaction()` as `SqlTransaction`, do not commit. A transaction the caller already committed or rolled back (but has not disposed) is no longer current, so the operation starts its own — the consumer calls bulk inserts from `finally` blocks in exactly that state.
4. Otherwise: `Database.CreateExecutionStrategy().ExecuteAsync(...)` wrapping `BeginTransactionAsync` → work → `CommitAsync`. Required for contexts configured with `EnableRetryOnFailure`.
5. `SqlBulkCopy` options: `CheckConstraints` (default on), `KeepIdentity` (opt-in); `BatchSize`, `BulkCopyTimeout` from `BulkOptions`; column mappings by ordinal to destination column names.

### 3.4 `BulkUpdate`

Within one transaction (§3.3):

1. `SELECT TOP 0 <matchOn cols>, <set cols> INTO #bulk_<guid:N> FROM <table>;` — exact column types.
2. `SqlBulkCopy` into the temp table with `KeepIdentity` (an identity PK carries its identity property into the temp table).
3. `SELECT TOP 1 1 FROM #bulk_x GROUP BY <matchOn cols> HAVING COUNT(*) > 1;` — any row → `ArgumentException`. Checked in SQL so SQL Server's own equality and collation decide what counts as a duplicate.
4. `UPDATE t SET t.[c1] = s.[c1], … FROM <table> AS t INNER JOIN #bulk_x AS s ON t.[k1] = s.[k1] AND …;` — return value is rows affected. Rows whose `matchOn` value is `NULL` match nothing.
5. `DROP TABLE #bulk_x;` on success. On failure the transaction rollback removes the temp table (its creation is transactional).

No values are ever placed in SQL text. Composite keys work unchanged.

### 3.5 Not included

Logging and an `ActivitySource`: SqlClient is already traced by the consumer's OpenTelemetry instrumentation. Add when a trace gap appears.

## 4. Behaviour changes to flag (breaking at runtime)

1. **`CheckConstraints = true` by default.** The old `SqlBulkCopy` default skipped CHECK/FK constraints and marked foreign keys untrusted. Inserts with orphaned FKs will now fail.
2. **DbContext connection and transaction.** A bulk write inside an open DbContext transaction now commits or rolls back with it (previously committed independently on a new connection).
3. **Throws instead of no-op** for every failure listed in §1.
4. **`GETUTCDATE()` substitution is opt-in** via `InsertSqlOptions.UtcNowColumns`.

## 5. Repository, packaging, CI

### Layout

```
Ntxinh.EFCore.Bulks.slnx
Directory.Build.props            # TreatWarningsAsErrors, nullable, analyzers
Directory.Packages.props         # TFM-conditional EF versions, transitive pinning on
src/Ntxinh.EFCore.Bulks/         # net8.0;net10.0 — references Microsoft.EntityFrameworkCore.SqlServer only
tests/Ntxinh.EFCore.Bulks.Tests/ # net8.0;net10.0 — xUnit v3, Testcontainers.MsSql
.github/workflows/ci.yml
CHANGELOG.md                     # includes 8.x/10.x → 2.0 migration guide
```

Deleted: `src/Ntxinh.EFCore.Bulks.Demo` (SQLite cannot exercise the SQL Server engine; tests replace it), `src/Ntxinh.EFCore.Bulks.Tests` (empty), the direct `Microsoft.EntityFrameworkCore` reference.

Removing the Demo removes `Microsoft.EntityFrameworkCore.Design` and the `NU1903` (`System.Security.Cryptography.Xml`) warning, unblocking `TreatWarningsAsErrors` and `CentralPackageTransitivePinningEnabled`.

### Central package versions

`Directory.Packages.props` pins EF Core per TFM with `Condition="'$(TargetFramework)' == 'net8.0'"` → **8.0.14** and `'net10.0'` → **10.0.0**: the *lowest* version the library supports, so consumers on any later patch restore cleanly (a higher floor than the consumer's own pin is a NU1605 downgrade error; QXM pins 8.0.14). Raise a floor only to fix a NuGet audit (NU190x) failure, to the lowest clean patch. *(Amended 2026-10-11; was "latest patch".)* Test packages (`xunit.v3`, `Microsoft.NET.Test.Sdk`, `Testcontainers.MsSql`) live in the same file.

### Packaging (SDK built-ins only)

- `GenerateDocumentationFile`, `IncludeSymbols` + `SymbolPackageFormat=snupkg`, SourceLink (SDK-included), `ContinuousIntegrationBuild` when `CI=true`.
- `EnablePackageValidation=true`; set `PackageValidationBaselineVersion=2.0.0` after the first release so accidental breaks fail the build.
- Drop `GeneratePackageOnBuild`; CI runs `dotnet pack`.
- Version from git tag: `v2.0.0` → `-p:Version=2.0.0`. The csproj `<Version>` is a local dev default only.
- Package metadata updated: description, tags (`bulk;sqlbulkcopy;efcore;sqlserver`).

### CI (`.github/workflows/ci.yml`, ubuntu-latest)

- On PR and push to `main`: restore → build (warnings as errors) → test (both TFMs, Docker available on hosted runners) → pack → upload `.nupkg` artifact.
- On tag `v*` (including prereleases such as `v2.0.0-rc.1`): same, then publish to nuget.org via **NuGet Trusted Publishing** (OIDC, `NuGet/login`). Fallback: `NUGET_API_KEY` repository secret. README documents the one-time trusted-publishing policy setup on nuget.org.

## 6. Tests

### Unit (no database; model built from a SQL Server-configured `DbContext` without connecting)

- `TableMap`: non-`dbo` schema, renamed columns, value converter, identity and computed exclusion, each unsupported mapping throws.
- `GetTableName<T>`: schema + delimiting, including a `]` in a name.
- `GetInsertSql<T>`: exact string; `UtcNowColumns` substitution; identity omitted.
- Expression parsing for `set` / `matchOn`: single member, anonymous object, invalid shapes throw, overlap throws.

### Integration (Testcontainers SQL Server 2022, one container per test run via assembly fixture)

- Insert round-trip: non-`dbo` schema, enum-to-string converter, nulls, `'` and unicode in strings, `DateTime` precision.
- `KeepIdentity` on and off.
- Joins caller transaction: rollback discards rows.
- Own transaction: mid-batch failure leaves zero rows.
- `CheckConstraints`: FK violation throws; with `false`, succeeds.
- Context with `EnableRetryOnFailure`: insert and update succeed (no strategy exception).
- `BulkUpdate`: PK match, composite key, non-PK `matchOn` updating multiple target rows, only `set` columns change, duplicate `matchOn` throws.
- Non-generic `BulkInsertAsync(Type, IEnumerable)`.
- `GetInsertSql` executed with `SqlParameter`s inserts the expected row.

## 7. Consumer migration (`azure_co4_backend`)

Separate PR, branch off `qa`, squash-merged per its CONTRIBUTING.md. Starts once **2.0.0-rc.1** is on nuget.org; QA validates the published prerelease, then the consumer moves to 2.0.0. *(Amended: rc step added so QA exercises the exact published package.)*

| Change | Sites | Approach |
|---|---|---|
| Package `8.0.14` → `2.0.0` | 1 | `Directory.Packages.props` |
| `GenerateInsertQuery<T>()` | 17 | New consumer extension `InsertSql<T>(this DbContext)` calling `GetInsertSql<T>` with `UtcNowColumns = [CreatedAt, UpdatedAt, CreatedOn, UpdatedOn]` — preserves old output |
| `BulkInsertAsync(…, options: null, connectionString: …)` | 6 | Drop the two arguments |
| `BulkInsertJob(Guid, Type, DataTable)` + queue sites | 1 + 9 | `BulkInsertJob(Guid, Type, IReadOnlyList<object>)`; handler calls non-generic `BulkInsertAsync` |
| `BulkInsertMultipleTablesAsync` | 5 | Consumer helper `BulkInsertTablesAsync`: own transaction (or the current one) → `BulkInsertAsync` per table → commit. Uses `Timeout = TimeSpan.Zero`, because the old `new SqlBulkCopyOptionsDto()` set `Timeout = 0` (no limit) |
| `GenerateBulkUpdateQuery` + `ExecuteSqlRawAsync` | 4 live | `BulkUpdateAsync(rows.DistinctBy(key), set: …, matchOn: …)`; `matchOn` explicit everywhere (two sites match on non-PK: `SlabBidHeaderTotals.SlabBidHeaderId`, `SlabOrderApplicationTotals.OrderApplicationId`). `DistinctBy` keeps the first row per key, which is what the old `CASE WHEN` produced |
| `GenerateDeleteQuery<T>().Item1.Replace(";", "")` | 1 | `$"DELETE FROM {AppDbContext.GetTableName<OrderBillingCode>()}"` |
| `SlabBidApplicationTotalRepositoryCharacterizationTests` | 1 | Update to new calls; serves as regression check |

Commented-out usages are left untouched.

### Verification

1. Consumer builds; characterization tests pass.
2. QA environment: run bid and order pricing recalculation for a sample of bids/orders; compare totals tables (row counts and values) before vs after.
3. Watch App Insights for new exceptions from bulk paths (expected surface: FK violations from `CheckConstraints`, previously silent no-ops).

## 8. Out of scope

Bulk upsert/MERGE, bulk delete, bulk read, identity output, graph insert, non-SQL Server providers, owned/complex types, TPH, logging. Each is added only when a consumer call site needs it.
