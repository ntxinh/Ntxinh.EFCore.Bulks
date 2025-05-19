# Ntxinh.EFCore.Bulks

Simple bulk implementation in .NET

# Nuget

https://www.nuget.org/packages/Ntxinh.EFCore.Bulks

## Features

- `BulkInsertAsync()` and `DataTableHelper.CreateDataTable<T>()`
- `BulkInsertAsync<T>()`
- `BulkInsertMultipleTablesAsync()`
- `GenerateInsertQuery<T>()`
- `GenerateUpdateQuery<T>()`
- `GenerateDeleteQuery<T>()`
- `GenerateSelectQuery<T>()`
- `GenerateCreateTableQuery<T>()`
- `GenerateDropTableQuery<T>()`
- `GenerateTruncateTableQuery<T>()`
- `GenerateBulkUpdateQuery<T>()`
- `GenerateMergeUpsertQueryExtensions<T>()`

## How to use

```cs
using Ntxinh.EFCore.Bulks;

namespace MyProject;

using (var _dbContext = new DemoDbContext())
{
    IEnumerable<DemoEntity> data = ...;
    await _dbContext.BulkInsertAsync<DemoEntity>(data, null, connectionString);
    await _dbContext.BulkInsertAsync(typeof(DemoEntity), DataTableHelper.CreateDataTable<DemoEntity>(data), null, connectionString);
    await _dbContext.BulkInsertMultipleTablesAsync([new BulkInsertMultipleTablesDto { ClrEntityType = typeof(DemoEntity), DataTable = DataTableHelper.CreateDataTable<DemoEntity>(data) }], null, connectionString);

    var createTableStr = _dbContext.GenerateCreateTableQuery<DemoEntity>();
    Console.WriteLine($"Script Create Table: {createTableStr}");

    var dropTableStr = _dbContext.GenerateDropTableQuery<DemoEntity>();
    Console.WriteLine($"Script Drop Table: {dropTableStr}");

    var truncateTableStr = _dbContext.GenerateTruncateTableQuery<DemoEntity>();
    Console.WriteLine($"Script Truncate Table: {truncateTableStr}");

    var insertQueryStr = _dbContext.GenerateInsertQuery<DemoEntity>();
    Console.WriteLine($"Script Insert Table: {insertQueryStr}");

    var (updateQueryStr, primaryKeyColumn) = _dbContext.GenerateUpdateQuery<DemoEntity>();
    Console.WriteLine($"Script Update Table: {updateQueryStr}");
    Console.WriteLine($"Script Primary Column Info: {primaryKeyColumn.ColumnName}, {primaryKeyColumn.DataType}");

    var (deleteQueryStr, primaryKeyColumn2) = _dbContext.GenerateDeleteQuery<DemoEntity>();
    Console.WriteLine($"Script Delete Table: {deleteQueryStr}");
    Console.WriteLine($"Script Primary Column Info: {primaryKeyColumn2.ColumnName}, {primaryKeyColumn2.DataType}");

    var selectQueryStr = _dbContext.GenerateSelectQuery<DemoEntity>();
    Console.WriteLine($"Script Select Table: {selectQueryStr}");

    var bulkUpdateData = new List<DemoEntity>
    {
        new DemoEntity
        {
            Id = 1,
            Formula = "New Formula 1",
            CategoryId = 1,
        },
        new DemoEntity
        {
            Id = 2,
            Formula = "New Formula 2",
            CategoryId = 2,
        },
    };
    List<string> bulkUpdateColumns = ["Formula", "CategoryId"];
    var bulkUpdateQueryStr = _dbContext.GenerateBulkUpdateQuery<DemoEntity>(bulkUpdateData, bulkUpdateColumns);
    Console.WriteLine($"Script Bulk Update: {bulkUpdateQueryStr}");
}
```

## How to push (Contributor only)

- Update version in `Ntxinh.EFCore.Bulks.csproj` first

```sh
cd src/Ntxinh.EFCore.Bulks
dotnet clean && dotnet build
cd bin/Debug
dotnet nuget push Ntxinh.EFCore.Bulks.8.0.x.nupkg --api-key API_KEY --source https://api.nuget.org/v3/index.json
# dotnet nuget locals --clear all
```

## TODO:

- [ ] `SqlTransaction`: `SqlBulkCopyOptions.UseInternalTransaction` & existing transaction
- [x] `SqlBulkCopyOptions`: `SqlBulkCopyOptions.KeepIdentity`
- [x] `BulkInsertAsync()` and `DataTableHelper.CreateDataTable<T>()`
- [x] `BulkInsertAsync<T>()`
- [x] `GenerateInsertQuery<T>()`
- [x] `GenerateUpdateQuery<T>()`
- [x] `GenerateDeleteQuery<T>()`
- [x] `GenerateSelectQuery<T>()`
- [x] `GenerateCreateTableQuery<T>()`
- [x] `GenerateDropTableQuery<T>()`
- [x] `GenerateTruncateTableQuery<T>()`
- [x] `GenerateBulkUpdateQuery<T>()`
- [x] `BulkInsertMultipleTablesAsync()`
- [x] `GenerateMergeUpsertQuery<T>()`
- [ ] `GenerateMergeUpsertQuery<T>(isMulipleData: true, useTempTable: true)`
