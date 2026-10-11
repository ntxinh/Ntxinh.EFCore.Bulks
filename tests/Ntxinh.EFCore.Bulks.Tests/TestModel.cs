using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Tests;

public enum OrderStatus
{
    Draft,
    Placed,
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Name { get; set; } = "";
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public int NameLength { get; set; }
    public byte[] Version { get; set; } = [];

    // Getter-only: EF does not map it.
    public string Display => $"{Name} ({Status})";
}

public class OrderLine
{
    public int OrderId { get; set; }
    public int LineNo { get; set; }
    public decimal Price { get; set; }
    public int Qty { get; set; }
}

public class AuditRow
{
    public string Message { get; set; } = "";
}

public class Owner
{
    public int Id { get; set; }
    public Address Address { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = "";
}

public class Animal
{
    public int Id { get; set; }
}

public class Dog : Animal
{
    public string Breed { get; set; } = "";
}

public class Secretive
{
    public int Id { get; set; }
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public static TestDbContext Create(string connectionString = "Server=unused;Database=unused", bool retryOnFailure = false) =>
        new(new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                if (retryOnFailure)
                {
                    sql.EnableRetryOnFailure();
                }
            })
            .Options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>();
        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("Orders", "sales");
            order.Property(x => x.Name).HasMaxLength(200);
            order.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            order.Property(x => x.Total).HasPrecision(18, 2);
            order.Property(x => x.Note).HasColumnName("note_text").HasMaxLength(200);
            order.Property(x => x.NameLength).HasComputedColumnSql("LEN([Name])");
            order.Property(x => x.Version).IsRowVersion();
            order.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
        modelBuilder.Entity<OrderLine>(line =>
        {
            line.HasKey(x => new { x.OrderId, x.LineNo });
            line.Property(x => x.Price).HasPrecision(18, 2);
        });
        modelBuilder.Entity<AuditRow>().HasNoKey().ToTable("Audit]Rows");
        modelBuilder.Entity<Owner>().OwnsOne(x => x.Address);
        modelBuilder.Entity<Animal>();
        modelBuilder.Entity<Dog>();
        modelBuilder.Entity<Secretive>().Property<string>("Secret");
    }
}
