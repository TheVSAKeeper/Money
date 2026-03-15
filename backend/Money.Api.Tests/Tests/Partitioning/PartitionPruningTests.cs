using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Money.Api.BackgroundServices;
using Money.Data.Sharding;
using System.Text.Json;

namespace Money.Api.Tests.Tests.Partitioning;

[TestFixture]
public class PartitionPruningTests
{
    [SetUp]
    public void Setup()
    {
        var scope = Integration.ServiceProvider.CreateScope();
        _factory = scope.ServiceProvider.GetRequiredService<ShardedDbContextFactory>();
        _dbClient = Integration.GetDatabaseClient();
    }

    private DatabaseClient _dbClient = null!;
    private ShardedDbContextFactory _factory = null!;

    /// <summary>
    /// Вставленная операция попадает в корректную месячную партицию.
    /// </summary>
    [Test]
    public async Task NewOperation_InsertedIntoCorrectPartition()
    {
        var user = _dbClient.WithUser();
        _dbClient.Save();

        var targetDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc);
        var operation = user.WithOperation().SetDate(targetDate);
        _dbClient.Save();

        await using var db = _dbClient.CreateApplicationDbContext();
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT tableoid::regclass::text
                              FROM operations
                              WHERE user_id = @userId AND id = @id
                              """;

        var userIdParam = command.CreateParameter();
        userIdParam.ParameterName = "userId";
        userIdParam.Value = user.Id;
        command.Parameters.Add(userIdParam);

        var idParam = command.CreateParameter();
        idParam.ParameterName = "id";
        idParam.Value = operation.Id;
        command.Parameters.Add(idParam);

        var result = (string?)await command.ExecuteScalarAsync();

        Assert.That(result, Is.EqualTo("operations_2026_03"),
            $"Ожидалась партиция operations_2026_03, фактически: {result}");
    }

    /// <summary>
    /// EXPLAIN показывает, что при фильтрации по дате PostgreSQL сканирует только нужные партиции.
    /// </summary>
    [Test]
    public async Task PartitionPruning_DateFilter_MinimalScans()
    {
        var user = _dbClient.WithUser();
        _dbClient.Save();

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var currentMonthSuffix = $"{now.Year}_{now.Month:D2}";
        var nextMonthSuffix = $"{monthStart.AddMonths(1).Year}_{monthStart.AddMonths(1).Month:D2}";

        await using var db = _dbClient.CreateApplicationDbContext();
        await using var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               EXPLAIN (FORMAT JSON, ANALYZE false)
                               SELECT * FROM operations
                               WHERE user_id = {user.Id}
                                 AND date BETWEEN '{monthStart:yyyy-MM-dd}' AND '{monthEnd:yyyy-MM-dd}'
                               """;

        var planJson = (string?)await command.ExecuteScalarAsync();

        Assert.That(planJson, Is.Not.Null.And.Not.Empty);

        using var doc = JsonDocument.Parse(planJson!);
        var planText = planJson!;

        Assert.That(planText, Does.Contain(currentMonthSuffix),
            $"EXPLAIN должен показывать сканирование партиции operations_{currentMonthSuffix}");

        Assert.That(planText, Does.Not.Contain(nextMonthSuffix),
            $"Partition pruning: партиция operations_{nextMonthSuffix} не должна сканироваться при фильтре по текущему месяцу");
    }

    /// <summary>
    /// PartitionMaintenanceService создаёт партиции для текущего и следующих 2 месяцев.
    /// </summary>
    [Test]
    public async Task PartitionMaintenance_CreatesFuturePartitions()
    {
        var fakeTime = new FakeTimeProvider(new(2026, 5, 15, 0, 0, 0, TimeSpan.Zero));

        using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
        var logger = loggerFactory.CreateLogger<PartitionMaintenanceService>();

        var service = new PartitionMaintenanceService(_factory, fakeTime, logger);

        await service.RunMaintenanceAsync(CancellationToken.None);

        foreach (var shardName in _factory.ShardNames)
        {
            await using var db = _factory.Create(shardName);
            await using var connection = db.Database.GetDbConnection();
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT tablename
                                  FROM pg_catalog.pg_tables
                                  WHERE tablename LIKE 'operations_%'
                                  ORDER BY tablename
                                  """;

            await using var reader = await command.ExecuteReaderAsync();
            var partitions = new List<string>();

            while (await reader.ReadAsync())
            {
                partitions.Add(reader.GetString(0));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(partitions, Has.Some.Matches<string>(p => p.Contains("2026_07")),
                    $"Шард {shardName}: ожидалась партиция operations_2026_07 после обслуживания");

                Assert.That(service.LastMaintenanceUtc.ContainsKey(shardName), Is.True,
                    $"Шард {shardName}: LastMaintenanceUtc должно быть установлено после обслуживания");
            }
        }
    }
}
