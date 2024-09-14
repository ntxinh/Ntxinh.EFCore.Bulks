using Microsoft.EntityFrameworkCore;

using Ntxinh.EFCore.Bulks;
using Ntxinh.EFCore.Bulks.Demo;

Console.WriteLine("Starting...");

using (var _dbContext = new DemoDbContext())
{
    _dbContext.Database.Migrate();

    var createTableStr = _dbContext.GenerateCreateTableQuery<DemoEntity>();
    Console.WriteLine($"Script Create Table: {createTableStr}");

    var dropTableStr = _dbContext.GenerateDropTableQuery<DemoEntity>();
    Console.WriteLine($"Script Drop Table: {dropTableStr}");

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
}

Console.WriteLine("Finished!");
Console.ReadLine();
