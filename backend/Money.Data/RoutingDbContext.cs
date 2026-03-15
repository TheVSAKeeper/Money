using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Money.Data.Entities;

namespace Money.Data;

public class RoutingDbContext(DbContextOptions<RoutingDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<ShardMapping> ShardMappings { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new ApplicationUserConfiguration());
        builder.ApplyConfiguration(new ShardMappingConfiguration());
    }
}
