using Microsoft.Data.SqlClient;

namespace Ntxinh.EFCore.Bulks;

public class SqlDto
{
    public string? TableName { get; set; }
    public ColumnMapDto? PrimaryKeyColumn { get; set; }
    public IEnumerable<ColumnMapDto>? ColumnMappings { get; set; }
    public IEnumerable<ColumnMapDto>? InvalidColumnMappings { get; set; }
    public SqlConnection? Connection { get; set; }
}
