# Changelog

## 2.0.0

A rewrite for production use. **Every public API changed.** Targets net8.0 (EF Core 8.0.14+) and net10.0 (EF Core 10.0.0+).

### Added

- `BulkUpdateAsync<T>(entities, set, matchOn?)` — temp table + `UPDATE … JOIN`; values never appear in SQL text.
- `GetTableName<T>()`.
- `BulkOptions.CheckConstraints` (default `true`) and `InsertSqlOptions.UtcNowColumns`.
- Write methods return the number of rows affected.

### Changed behaviour

- Uses the DbContext's own connection; joins `Database.CurrentTransaction`, otherwise runs in its own transaction inside the execution strategy (`EnableRetryOnFailure` works).
- Throws instead of silently doing nothing (unmapped entity, missing connection string, `Persist Security Info=false`, …).
- CHECK and FOREIGN KEY constraints are enforced by default; set `CheckConstraints = false` for the old behaviour.
- Table names are schema-qualified and delimited (`[sales].[Orders]`); value converters are applied; computed and rowversion columns are skipped.
- `GETUTCDATE()` for `CreatedAt` / `UpdatedAt` / `CreatedOn` / `UpdatedOn` is no longer automatic: list the columns in `InsertSqlOptions.UtcNowColumns`.
- `GetInsertSql` keeps non-identity primary keys (the old generator always dropped the first key column).

### Migration from 8.x / 10.x

| 8.x / 10.x | 2.0 |
|---|---|
| `BulkInsertAsync<T>(data, options, connectionString, ct)` | `BulkInsertAsync<T>(data, options, ct)` |
| `BulkInsertAsync(type, DataTableHelper.CreateDataTable<T>(data), …)` | `BulkInsertAsync(type, data, options, ct)` |
| `BulkInsertMultipleTablesAsync(dtos, …)` | `BeginTransactionAsync` → one `BulkInsertAsync` per table → `CommitAsync` |
| `SqlBulkCopyOptionsDto` | `BulkOptions` (note: `new SqlBulkCopyOptionsDto()` meant `Timeout = 0`, i.e. no limit — use `Timeout = TimeSpan.Zero`) |
| `GenerateInsertQuery<T>()` | `GetInsertSql<T>(new InsertSqlOptions { UtcNowColumns = ["CreatedAt", "UpdatedAt", "CreatedOn", "UpdatedOn"] })` for identical output |
| `GenerateBulkUpdateQuery<T>(rows, [key, col1, col2])` + `ExecuteSqlRawAsync` | `BulkUpdateAsync(rows.DistinctBy(x => x.Key), set: x => new { x.Col1, x.Col2 }, matchOn: x => x.Key)` |
| `GenerateDeleteQuery<T>().Item1` | `$"DELETE FROM {db.GetTableName<T>()}"` or EF `ExecuteDeleteAsync` |
| `GenerateUpdateQuery`, `GenerateSelectQuery`, `GenerateMergeUpsertQuery`, `GenerateCreateTableQuery`, `GenerateDropTableQuery`, `GenerateTruncateTableQuery` | Removed |
| `DataTableHelper`, `SqlBulkCopyHelper`, `ExtractDbContext`, `ObjectExtensions`, `TypeDefaultValue`, `Helpers`, `Constants`, DTO classes | Removed |
