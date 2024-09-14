using System.ComponentModel.DataAnnotations.Schema;

namespace Ntxinh.EFCore.Bulks.Demo;

public class DemoEntity
{
    public int Id { get; set; }
    public int ProcessId { get; set; }
    public int CategoryId { get; set; }
    public string Formula { get; set; }
    public decimal DefaultValue { get; set; }
    public bool Active { get; set; }

    [NotMapped]
    public int DomainEvents { get; set; }
}
