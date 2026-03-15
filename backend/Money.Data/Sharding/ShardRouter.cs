using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Money.Data.Sharding;

public class ShardRouter
{
    private readonly SortedDictionary<int, string> _ring = [];
    private readonly ILogger<ShardRouter> _logger;

    public ShardRouter(IOptions<ShardingOptions> options, ILogger<ShardRouter> logger)
    {
        _logger = logger;

        var shardingOptions = options.Value;

        _logger.LogInformation("Инициализация hash ring: {ShardCount} шардов, {VirtualNodes} виртуальных узлов на шард",
            shardingOptions.Shards.Count,
            shardingOptions.VirtualNodes);

        foreach (var shard in shardingOptions.Shards.Select(x=>x.Name))
        {
            for (var v = 0; v < shardingOptions.VirtualNodes; v++)
            {
                _ring[Hash($"{shard}-{v}")] = shard;
            }

            _logger.LogDebug("Шард {ShardName} добавлен в hash ring ({VirtualNodes} виртуальных узлов)",
                shard,
                shardingOptions.VirtualNodes);
        }

        _logger.LogInformation("Hash ring построен: {TotalNodes} узлов для шардов [{ShardNames}]",
            _ring.Count,
            string.Join(", ", _ring.Values.Distinct()));
    }

    public string ResolveShard(int userId)
    {
        return ResolveByKey(userId.ToString());
    }

    public string ResolveShard(Guid authUserId)
    {
        return ResolveByKey(authUserId.ToString());
    }

    public IEnumerable<string> GetAllShardNames()
    {
        return _ring.Values.Distinct();
    }

    private static int Hash(string input)
    {
        return BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(input)), 0) & 0x7FFFFFFF;
    }

    private string ResolveByKey(string key)
    {
        var hash = Hash(key);
        var ringKey = _ring.Keys.FirstOrDefault(k => k >= hash);
        var shardName = ringKey != 0 ? _ring[ringKey] : _ring[_ring.Keys.First()];

        _logger.LogDebug("Маршрутизация ключа {Key}: hash={Hash} -> шард {ShardName}",
            key,
            hash,
            shardName);

        return shardName;
    }
}
