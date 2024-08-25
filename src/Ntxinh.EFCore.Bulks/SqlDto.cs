using Microsoft.Data.SqlClient;

namespace Ntxinh.EFCore.Bulks;

public class SqlDto
{
    public string? TableName { get; set; }
    public KeyValuePair<string, string>? PrimaryKeyColumn { get; set; }
    public IDictionary<string, string>? ColumnMappings { get; set; }
    public SqlConnection? Connection { get; set; }
}