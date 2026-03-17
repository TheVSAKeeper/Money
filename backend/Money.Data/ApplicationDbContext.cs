using Money.Data.Entities;
using System.Reflection;

namespace Money.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<DomainUser> DomainUsers { get; set; } = null!;
    public DbSet<Operation> Operations { get; set; } = null!;
    public DbSet<FastOperation> FastOperations { get; set; } = null!;
    public DbSet<RegularOperation> RegularOperations { get; set; } = null!;
    public DbSet<Place> Places { get; set; } = null!;
    public DbSet<Debt> Debts { get; set; } = null!;
    public DbSet<DebtOwner> DebtOwners { get; set; } = null!;
    public DbSet<Car> Cars { get; set; } = null!;
    public DbSet<CarEvent> CarEvents { get; set; } = null!;
    public DbSet<OutboxEvent> OutboxEvents { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly(),
            t => t.Namespace?.StartsWith("Money.Data.Entities", StringComparison.Ordinal) == true
                 && t != typeof(ApplicationUserConfiguration)
                 && t != typeof(ShardMappingConfiguration));
    }
}
