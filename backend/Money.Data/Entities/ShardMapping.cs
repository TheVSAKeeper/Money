namespace Money.Data.Entities;

public class ShardMapping
{
    public int UserId { get; set; }
    public string ShardName { get; set; } = "";
    public DateTime AssignedAt { get; set; }
}

public class ShardMappingConfiguration : IEntityTypeConfiguration<ShardMapping>
{
    public void Configure(EntityTypeBuilder<ShardMapping> builder)
    {
        builder.HasKey(x => new { x.UserId, x.ShardName });

        builder.Property(x => x.ShardName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.AssignedAt)
            .IsRequired();
    }
}
