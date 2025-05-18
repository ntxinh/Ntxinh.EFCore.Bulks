using System.Data;

namespace Ntxinh.EFCore.Bulks;

public class SqlBulkCopyDto
{
    public string TableName { get; set; }
    public DataTable Data { get; set; }
    public IEnumerable<ColumnMapDto> ColumnMappings { get; set; }
}

public class SqlBulkCopyOptionsDto
{
    public int BatchSize { get; set; }
    public int Timeout { get; set; }
    public bool KeepIdentity { get; set; }

    public SqlBulkCopyOptionsDto()
    {
        Timeout = 0; // Default 30
        BatchSize = 0; // Default 0
        KeepIdentity = false; // Default false
    }
}
