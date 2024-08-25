using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ntxinh.EFCore.Bulks;

public static class DbContextExtensions
{
    public static SqlDto ExtractDbContext(this DbContext dbContext, Type clrEntityType)
    {
        try
        {
            var entityType = dbContext.Model.FindEntityType(clrEntityType);
            if (entityType is null)
            {
                return new SqlDto
                {
                    TableName = null,
                    PrimaryKeyColumn = null,
                    ColumnMappings = null,
                    Connection = null,
                };
            }
            var schema = entityType.GetSchema();
            var tableName = entityType.GetTableName();
            if (tableName is null)
            {
                return new SqlDto
                {
                    TableName = null,
                    PrimaryKeyColumn = null,
                    ColumnMappings = null,
                    Connection = null,
                };
            }

            var storeObjectIdentifier = StoreObjectIdentifier.Table(tableName, schema);

            var columnMappings = entityType
                .GetProperties()
                .ToDictionary(k => k.GetDefaultColumnName(), v => v.GetColumnName(storeObjectIdentifier));

            // Primary Key
            var primaryKey = entityType.FindPrimaryKey();
            var primaryKeyColumn = primaryKey?.Properties
                .ToDictionary(k => k.GetDefaultColumnName(), v => v.GetColumnName(storeObjectIdentifier))
                .FirstOrDefault();
            // .Select(x => x.GetColumnName(storeObjectIdentifier))
            // .ToList();
            // var primaryKeyColumnName = primaryKeyColumns.FirstOrDefault();

            // Connection
            var sqlConnection = dbContext.Database.GetDbConnection() as SqlConnection;

            return new SqlDto
            {
                TableName = tableName,
                PrimaryKeyColumn = primaryKeyColumn,
                ColumnMappings = columnMappings,
                Connection = sqlConnection,
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return new SqlDto
            {
                TableName = null,
                PrimaryKeyColumn = null,
                ColumnMappings = null,
                Connection = null,
            };
        }
    }
}