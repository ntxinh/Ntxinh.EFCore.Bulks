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
    List<string> bulkUpdateColumns = ["Id", "Formula", "CategoryId"];
    var bulkUpdateQueryStr = _dbContext.GenerateBulkUpdateQuery<DemoEntity>(bulkUpdateData, bulkUpdateColumns);
    Console.WriteLine($"Script Bulk Update: {bulkUpdateQueryStr}");
}

Console.WriteLine("Finished!");
Console.ReadLine();
