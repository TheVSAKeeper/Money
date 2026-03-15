namespace Money.Data.Sharding;

public class ShardingOptions
{
    public List<ShardConfig> Shards { get; set; } = [];
    public int VirtualNodes { get; set; } = 150;
}

public class ShardConfig
{
    public string Name { get; set; } = "";
    public string ConnectionName { get; set; } = "";
}
