using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Collections.Frozen;

namespace Money.Data.Sharding;

public sealed class ShardedDbContextFactory
{
    private readonly FrozenDictionary<string, NpgsqlDataSource> _dataSources;
    private readonly ILogger<ShardedDbContextFactory> _logger;

    public ShardedDbContextFactory(
        IServiceProvider sp,
        IOptions<ShardingOptions> options,
        ILogger<ShardedDbContextFactory> logger)
    {
        _logger = logger;

        _dataSources = options.Value.Shards.ToFrozenDictionary(s => s.Name,
            s => sp.GetRequiredKeyedService<NpgsqlDataSource>(s.Name));

        _logger.LogInformation("Фабрика шардов инициализирована: {ShardCount} источников данных [{ShardNames}]",
            _dataSources.Count,
            string.Join(", ", _dataSources.Keys));
    }

    public IEnumerable<string> ShardNames => _dataSources.Keys;

    public ApplicationDbContext Create(string shardName)
    {
        if (!_dataSources.TryGetValue(shardName, out var value))
        {
            _logger.LogError("Запрошен несуществующий шард {ShardName}. Доступные шарды: [{ShardNames}]",
                shardName,
                string.Join(", ", _dataSources.Keys));

            throw new ArgumentException($"Шард '{shardName}' не найден.", nameof(shardName));
        }

        var builder = new DbContextOptionsBuilder<ApplicationDbContext>();

        builder.UseNpgsql(value, npgsql =>
        {
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory");
        });

        builder.UseSnakeCaseNamingConvention();
        builder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));

        _logger.LogDebug("Создан DbContext для шарда {ShardName}", shardName);

        return new(builder.Options);
    }
}
