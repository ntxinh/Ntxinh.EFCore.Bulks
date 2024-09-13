using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks;

public static class GenerateDeleteQueryExtensions
{
    public static (string, string) GenerateDeleteQuery<T>(this DbContext dbContext) where T : class
    {
        // Extract data
        var columnMappingsResult = dbContext.ExtractDbContext(typeof(T));

        // Destruction data
        var tableName = columnMappingsResult.TableName;
        var primaryKeyColumnName = columnMappingsResult.PrimaryKeyColumn;
        var columnMappings = columnMappingsResult.ColumnMappings;
        var connection = columnMappingsResult.Connection;

        // Validate extract data
        if (
            string.IsNullOrEmpty(tableName)
            || primaryKeyColumnName is null || primaryKeyColumnName.Equals(default(KeyValuePair<string, string>))
            || columnMappings is null || !columnMappings.Any()
            || connection is null
        ) return (string.Empty, string.Empty);

        // Build query string
        var sql = new StringBuilder($"DELETE FROM {tableName}{Constants.NewLine}WHERE ");
        // var sql = new StringBuilder($"DELETE FROM {tableName}{Constants.NewLine}WHERE [{primaryKeyColumnName.Value.Value}] IN @{primaryKeyColumnName.Value.Key};");

        return (sql.ToString(), primaryKeyColumnName.Value.Value);
    }
}
