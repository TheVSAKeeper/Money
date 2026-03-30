using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Money.Api.BackgroundServices;
using Money.Api.Services.Analytics;
using Money.Api.Services.Lakehouse;
using Money.Business;
using Money.Business.Interfaces;
using Money.Common.Exceptions;
using Money.Data;
using Money.Data.Extensions;
using OpenIddict.Validation.AspNetCore;

namespace Money.Api.Controllers;

#if DEBUG

/// <summary>
/// Контроллер только для разработки. Генерация тестовых данных для демонстрации всех стадий.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
[Route("[controller]")]
public class DevelopmentController(
    RequestEnvironment environment,
    ApplicationDbContext context,
    ClickHouseSyncService clickHouseSyncService,
    Neo4jSyncService neo4jSyncService,
    LakehouseSyncService lakehouseSyncService,
    LakehouseTransformService lakehouseTransformService,
    OutboxCursorService outboxCursorService,
    IEmailQueueService emailQueueService,
    ILogger<DevelopmentController> logger) : ControllerBase
{
    private static readonly string[] Consumers =
        [ClickHouseSyncService.ConsumerName, Neo4jSyncService.ConsumerName, LakehouseSyncService.ConsumerName];

    /// <summary>
    /// Полный сид всех данных пользователя для отладки всех стадий (1–11).
    /// Полная очистка + пересоздание + sync downstream.
    /// </summary>
    [HttpPost("Seed")]
    [ProducesResponseType(typeof(SeedResponse), StatusCodes.Status200OK)]
    public async Task<SeedResponse> Seed(CancellationToken ct = default)
    {
        var userId = environment.UserId;
        var shardName = environment.ShardName!;
        var now = DateTime.UtcNow;

        logger.LogInformation("Seed started for user {UserId} on shard {Shard}", userId, shardName);

        // 1. Clear all user data + outbox + cursors
        await ClearUserDataAsync(userId, shardName, ct);

        // 2. Ensure partitions exist for the full date range
        await EnsurePartitionsAsync(now, ct);

        // 3. Generate all entities
        var dbUser = await context.DomainUsers.SingleOrDefaultAsync(x => x.Id == userId, ct)
                     ?? throw new BusinessException("Извините, но пользователь не найден.");

        var categories = DatabaseSeeder.SeedCategories(userId, out var lastCategoryIndex);
        var (operations, places) = DatabaseSeeder.SeedOperations(userId, categories, now);
        var fastOperations = DatabaseSeeder.SeedFastOperations(userId, categories);
        var regularOperations = DatabaseSeeder.SeedRegularOperations(userId, categories, now);
        var debtOwners = DatabaseSeeder.SeedDebtOwners(userId);
        var debts = DatabaseSeeder.SeedDebts(userId, debtOwners, now);
        var cars = DatabaseSeeder.SeedCars(userId);
        var carEvents = DatabaseSeeder.SeedCarEvents(userId, cars, now);

        // Update ID counters
        dbUser.NextCategoryId = lastCategoryIndex + 1;
        dbUser.NextOperationId = operations[^1].Id + 1;
        dbUser.NextPlaceId = places[^1].Id + 1;
        dbUser.NextFastOperationId = fastOperations[^1].Id + 1;
        dbUser.NextRegularOperationId = regularOperations[^1].Id + 1;
        dbUser.NextDebtOwnerId = debtOwners[^1].Id + 1;
        dbUser.NextDebtId = debts[^1].Id + 1;
        dbUser.NextCarId = cars[^1].Id + 1;
        dbUser.NextCarEventId = carEvents[^1].Id + 1;

        await context.Categories.AddRangeAsync(categories, ct);
        await context.Places.AddRangeAsync(places, ct);
        await context.Operations.AddRangeAsync(operations, ct);
        await context.FastOperations.AddRangeAsync(fastOperations, ct);
        await context.RegularOperations.AddRangeAsync(regularOperations, ct);
        await context.DebtOwners.AddRangeAsync(debtOwners, ct);
        await context.Debts.AddRangeAsync(debts, ct);
        await context.Cars.AddRangeAsync(cars, ct);
        await context.CarEvents.AddRangeAsync(carEvents, ct);

        // 4. SaveChanges — AnalyticsInterceptor generates OutboxEvents
        await context.SaveChangesAsync(ct);

        // 5. Email queue
        var emailResult = await SeedEmailQueueInternal();

        // 6. Downstream sync
        await clickHouseSyncService.RunSyncAsync(ct);
        await neo4jSyncService.RunSyncAsync(ct);
        await lakehouseSyncService.RunSyncAsync(ct);

        // 7. Lakehouse transforms
        await lakehouseTransformService.TransformBronzeToSilverAsync(ct);
        await lakehouseTransformService.TransformSilverToGoldAsync(ct);

        logger.LogInformation(
            "Seed completed for user {UserId}: {Cats} categories, {Ops} operations, {Debts} debts",
            userId, categories.Count, operations.Count, debts.Count);

        return new SeedResponse
        {
            Categories = categories.Count,
            Operations = operations.Count,
            FastOperations = fastOperations.Count,
            RegularOperations = regularOperations.Count,
            Places = places.Count,
            DebtOwners = debtOwners.Count,
            Debts = debts.Count,
            Cars = cars.Count,
            CarEvents = carEvents.Count,
            EmailQueued = emailResult.Queued,
            EmailRetry = emailResult.Retry,
            EmailDeadLetter = emailResult.DeadLetter,
        };
    }

    private async Task ClearUserDataAsync(int userId, string shardName, CancellationToken ct)
    {
        context.CarEvents.RemoveRange(context.CarEvents.IgnoreQueryFilters().IsUserEntity(userId));
        context.Cars.RemoveRange(context.Cars.IgnoreQueryFilters().IsUserEntity(userId));
        context.Debts.RemoveRange(context.Debts.IgnoreQueryFilters().IsUserEntity(userId));
        context.DebtOwners.RemoveRange(context.DebtOwners.IgnoreQueryFilters().IsUserEntity(userId));
        context.FastOperations.RemoveRange(context.FastOperations.IgnoreQueryFilters().IsUserEntity(userId));
        context.RegularOperations.RemoveRange(context.RegularOperations.IgnoreQueryFilters().IsUserEntity(userId));
        context.Operations.RemoveRange(context.Operations.IgnoreQueryFilters().IsUserEntity(userId));
        context.Categories.RemoveRange(context.Categories.IgnoreQueryFilters().IsUserEntity(userId));
        context.Places.RemoveRange(context.Places.IgnoreQueryFilters().IsUserEntity(userId));
        context.OutboxEvents.RemoveRange(context.OutboxEvents);

        await context.SaveChangesAsync(ct);

        foreach (var consumer in Consumers)
        {
            await outboxCursorService.SetCursorAsync(consumer, shardName, 0);
        }
    }

    private async Task EnsurePartitionsAsync(DateTime now, CancellationToken ct)
    {
        var database = context.Database;

        for (var monthOffset = -5; monthOffset <= 2; monthOffset++)
        {
            var month = now.AddMonths(monthOffset);
            var rangeStart = new DateOnly(month.Year, month.Month, 1);
            var rangeEnd = rangeStart.AddMonths(1);
            var partitionName = $"operations_{rangeStart.Year}_{rangeStart.Month:D2}";

            await database.ExecuteSqlRawAsync(
                $"""
                 CREATE TABLE IF NOT EXISTS {partitionName}
                     PARTITION OF operations
                     FOR VALUES FROM ('{rangeStart:yyyy-MM-dd}') TO ('{rangeEnd:yyyy-MM-dd}')
                 """, ct);
        }
    }

    private async Task<(int Queued, int Retry, int DeadLetter)> SeedEmailQueueInternal()
    {
        var users = new[]
        {
            "ivan.petrov", "anna.smirnova", "dmitry.kozlov", "elena.novikova", "sergey.popov",
        };

        var subjects = new[]
        {
            "Подтверждение регистрации", "Сброс пароля", "Уведомление о входе",
            "Подтверждение email", "Добро пожаловать!",
        };

        foreach (var (user, subject) in users.Zip(subjects))
        {
            await emailQueueService.EnqueueAsync(new MailMessage($"{user}@example.com",
                subject,
                $"Здравствуйте, {user}! {subject}."));
        }

        await emailQueueService.EnqueueRetryAsync(new MailEnvelope
        {
            Message = new("retry.user@example.com", "Повторная попытка", "Письмо ожидает повторной отправки."),
            RetryCount = 1,
        });

        await emailQueueService.EnqueueDeadLetterAsync(new MailEnvelope
        {
            Message = new("dead.letter@example.com", "Недоставленное письмо", "Превышен лимит попыток."),
            RetryCount = 3,
        });

        return (users.Length, 1, 1);
    }

    public class SeedResponse
    {
        public int Categories { get; set; }
        public int Operations { get; set; }
        public int FastOperations { get; set; }
        public int RegularOperations { get; set; }
        public int Places { get; set; }
        public int DebtOwners { get; set; }
        public int Debts { get; set; }
        public int Cars { get; set; }
        public int CarEvents { get; set; }
        public int EmailQueued { get; set; }
        public int EmailRetry { get; set; }
        public int EmailDeadLetter { get; set; }
    }
}

#endif
