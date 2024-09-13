# Ntxinh.EFCore.Bulks

Simple bulk implementation in .NET

# Nuget

https://www.nuget.org/packages/Ntxinh.EFCore.Bulks

## Features

- `BulkInsertAsync()` and `DataTableHelper.CreateDataTable<T>()`
- `BulkInsertAsync<T>()`
- `GenerateInsertQuery<T>()`
- `GenerateUpdateQuery<T>()`
- `GenerateDeleteQuery<T>()`
- `GenerateSelectQuery<T>()`

## How to use

```cs
using Ntxinh.EFCore.Bulks;

namespace MyProject;

public class Test()
{
    IEnumerable<Blog> data = ...;
    await _dbContext.BulkInsertAsync<Blog>(data);
    await _dbContext.BulkInsertAsync(typeof(Blog), DataTableHelper.CreateDataTable<Blog>(data));

    var insertQueryStr = _dbContext.GenerateInsertQuery<Blog>();
    var updateQueryStr = _dbContext.GenerateUpdateQuery<Blog>();
    var (deleteQueryStr, primaryKeyColumnName) = _dbContext.GenerateDeleteQuery<Blog>();
    var selectQueryStr = _dbContext.GenerateSelectQuery<Blog>();
}
```

## How to push (Contributor only)

- Update version in `Ntxinh.EFCore.Bulks.csproj` first

```sh
cd src/Ntxinh.EFCore.Bulks
dotnet build
cd bin/Debug
dotnet nuget push Ntxinh.EFCore.Bulks.8.0.x.nupkg --api-key API_KEY --source https://api.nuget.org/v3/index.json
# dotnet nuget locals --clear all
```

## TODO:

- [ ] `GenerateSelectQuery<T>()`
- [ ] `GenerateCreateTableQuery<T>()`
- [ ] `GenerateDropTableQuery<T>()`
- [ ] `SqlTransaction`
- [ ] `SqlBulkCopyOptions`
- [ ] SQL MERGE
- [x] `BulkInsertAsync()` and `DataTableHelper.CreateDataTable<T>()`
- [x] `BulkInsertAsync<T>()`
- [x] `GenerateInsertQuery<T>()`
- [x] `GenerateUpdateQuery<T>()`
- [x] `GenerateDeleteQuery<T>()`
- [x] `GenerateSelectQuery<T>()`
