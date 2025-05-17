using System.Data;

namespace Ntxinh.EFCore.Bulks;

public class BulkInsertMultipleTablesDto
{
    public Type ClrEntityType { get; set; }
    public DataTable DataTable { get; set; }
}
