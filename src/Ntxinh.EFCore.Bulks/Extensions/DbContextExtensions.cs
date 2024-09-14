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
                    InvalidColumnMappings = null,
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
                    InvalidColumnMappings = null,
                    Connection = null,
                };
            }

            var storeObjectIdentifier = StoreObjectIdentifier.Table(tableName, schema);

            var columnMappings = entityType
                .GetProperties()
                .Select(x => new ColumnMapDto
                {
                    EntityColumn = new ColumnInfoDto
                    {
                        ColumnName = x.GetDefaultColumnName(),
                        DataType = x.PropertyInfo.PropertyType.ToString(),
                        IsNullable = x.IsNullable,
                    },
                    SqlColumn = new ColumnInfoDto
                    {
                        ColumnName = x.GetColumnName(storeObjectIdentifier),
                        DataType = x.GetColumnType(),
                        IsNullable = x.IsColumnNullable(),
                    },
                });
            // .ToDictionary(k => k.GetDefaultColumnName(), v => v.GetColumnName(storeObjectIdentifier));

            var validColumnNames = columnMappings.Select(x => x.EntityColumn.ColumnName).ToList();
            var invalidColumnMappings = clrEntityType
                .GetProperties()
                .Where(x => !validColumnNames.Contains(x.Name))
                .Select(x => new ColumnMapDto
                {
                    EntityColumn = new ColumnInfoDto
                    {
                        ColumnName = x.Name,
                        DataType = x.PropertyType.ToString(),
                        IsNullable = false,
                    },
                    SqlColumn = null,
                });

            // Primary Key
            var primaryKey = entityType.FindPrimaryKey();
            var primaryKeyColumn = primaryKey?.Properties
                .Select(x => new ColumnMapDto
                {
                    EntityColumn = new ColumnInfoDto
                    {
                        ColumnName = x.GetDefaultColumnName(),
                        DataType = x.PropertyInfo.PropertyType.ToString(),
                        IsNullable = x.IsNullable,
                    },
                    SqlColumn = new ColumnInfoDto
                    {
                        ColumnName = x.GetColumnName(storeObjectIdentifier),
                        DataType = x.GetColumnType(),
                        IsNullable = x.IsColumnNullable(),
                    },
                })
                // .ToDictionary(k => k.GetDefaultColumnName(), v => v.GetColumnName(storeObjectIdentifier))
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
                InvalidColumnMappings = invalidColumnMappings,
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
                InvalidColumnMappings = null,
                Connection = null,
            };
        }
    }
}
