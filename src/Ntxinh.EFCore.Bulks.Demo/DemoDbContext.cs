using Microsoft.EntityFrameworkCore;

namespace Ntxinh.EFCore.Bulks.Demo;

public class DemoDbContext : DbContext
{
    // private const string _connectionString = "Server=tcp:?,1433;Initial Catalog=?;Persist Security Info=True;User ID=?;Password=?;MultipleActiveResultSets=False;Encrypt=True;Trusted_Connection=true;TrustServerCertificate=True;Connection Timeout=30;"
    private const string _connectionString = "Data Source=?;Initial Catalog=?;Persist Security Info=True;User ID=?;Password=?;Integrated Security=False;ConnectRetryCount=0;MultipleActiveResultSets=True";

    public DbSet<DemoEntity> DemoEntities { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer(_connectionString);

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.Entity<DemoEntity>().HasKey(x => x.Id);

        modelBuilder.Entity<DemoEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.ToTable("tblDemoEntity");
            entity.Property(e => e.ProcessId).HasColumnName("Process_Id");
            entity.Property(e => e.CategoryId).HasColumnName("Category_Id");
        });

        base.OnModelCreating(modelBuilder);
    }
}
