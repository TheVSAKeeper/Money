using Microsoft.EntityFrameworkCore.Design;

namespace Money.Data;

public class ApplicationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=money_design;Username=postgres");
        optionsBuilder.UseSnakeCaseNamingConvention();
        return new(optionsBuilder.Options);
    }
}

public class RoutingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<RoutingDbContext>
{
    public RoutingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RoutingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=money_routing_design;Username=postgres");
        optionsBuilder.UseSnakeCaseNamingConvention();
        optionsBuilder.UseOpenIddict();
        return new(optionsBuilder.Options);
    }
}
