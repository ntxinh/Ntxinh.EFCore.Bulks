# Ntxinh.EFCore.Bulks

Simple bulk implementation in .NET

# Nuget

https://www.nuget.org/packages/Ntxinh.EFCore.Bulks

## Features

- `BulkInsertAsync<T>()`
- `GenerateInsertQuery<T>()`
- `GenerateUpdateQuery<T>()`
- `GenerateDeleteQuery<T>()`

## How to use

```cs
IEnumerable<Blog> data = ...;
await _dbContext.BulkInsertAsync<Blog>(data);

var insertQueryStr = _dbContext.GenerateInsertQuery<Blog>();
var updateQueryStr = _dbContext.GenerateUpdateQuery<Blog>();
var deleteQueryStr = _dbContext.GenerateDeleteQuery<Blog>();
```

## How to push (Contributor only)

```sh
cd src/Ntxinh.EFCore.Bulks
dotnet build
cd bin/Debug
dotnet nuget push Ntxinh.EFCore.Bulks.1.0.0.nupkg --api-key API_KEY --source https://api.nuget.org/v3/index.json
```

## TODO:

- [ ] `GenerateSelectQuery<T>()`
- [ ] `GenerateCreateTableQuery<T>()`
- [ ] `GenerateDropTableQuery<T>()`
- [ ] `SqlTransaction`
- [ ] `SqlBulkCopyOptions`
- [ ] SQL MERGE
- [x] `BulkInsertAsync<T>()`
- [x] `GenerateInsertQuery<T>()`
- [x] `GenerateUpdateQuery<T>()`
- [x] `GenerateDeleteQuery<T>()`
