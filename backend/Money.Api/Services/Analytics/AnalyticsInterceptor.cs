using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Money.Data;
using Money.Data.Entities;
using System.Text.Json;
using DataCategory = Money.Data.Entities.Category;
using DataDebt = Money.Data.Entities.Debt;
using DataDebtOwner = Money.Data.Entities.DebtOwner;
using DataOperation = Money.Data.Entities.Operation;
using DataPlace = Money.Data.Entities.Place;

namespace Money.Api.Services.Analytics;

public class AnalyticsInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not ApplicationDbContext db)
        {
            return new(result);
        }

        var operations = db.ChangeTracker.Entries<DataOperation>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var debts = db.ChangeTracker.Entries<DataDebt>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var categoryEntries = db.ChangeTracker.Entries<DataCategory>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        if (operations.Count == 0 && debts.Count == 0 && categoryEntries.Count == 0)
        {
            return new(result);
        }

        var categories = db.ChangeTracker.Entries<DataCategory>()
            .ToDictionary(e => (e.Entity.UserId, e.Entity.Id), e => e.Entity);

        var places = db.ChangeTracker.Entries<DataPlace>()
            .ToDictionary(e => (e.Entity.UserId, e.Entity.Id), e => e.Entity);

        var owners = db.ChangeTracker.Entries<DataDebtOwner>()
            .ToDictionary(e => (e.Entity.UserId, e.Entity.Id), e => e.Entity);

        foreach (var entry in operations)
        {
            var op = entry.Entity;
            var action = entry.State switch
            {
                EntityState.Added => "added",
                EntityState.Deleted => "deleted",
                _ => "modified",
            };

            categories.TryGetValue((op.UserId, op.CategoryId), out var cat);
            cat ??= op.Category;

            string? placeName = null;

            if (op.PlaceId.HasValue)
            {
                places.TryGetValue((op.UserId, op.PlaceId.Value), out var place);
                placeName = place?.Name;
            }

            var payload = new OperationPayload(op.UserId,
                op.Id,
                op.CategoryId,
                cat?.Name ?? "",
                cat?.TypeId ?? 0,
                op.Sum,
                DateOnly.FromDateTime(op.Date),
                placeName,
                op.Comment,
                action);

            db.OutboxEvents.Add(new()
            {
                EventType = OutboxEvent.OperationType,
                Payload = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow,
            });
        }

        foreach (var entry in debts)
        {
            var debt = entry.Entity;
            var action = entry.State switch
            {
                EntityState.Added => "added",
                EntityState.Deleted => "deleted",
                _ => "modified",
            };

            owners.TryGetValue((debt.UserId, debt.OwnerId), out var owner);
            owner ??= debt.Owner;

            var payload = new DebtPayload(debt.UserId,
                debt.Id,
                owner?.Name ?? "",
                debt.TypeId,
                debt.Sum,
                debt.PaySum,
                debt.StatusId,
                DateOnly.FromDateTime(debt.Date),
                debt.IsDeleted || entry.State == EntityState.Deleted,
                action);

            db.OutboxEvents.Add(new()
            {
                EventType = OutboxEvent.DebtType,
                Payload = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow,
            });
        }

        foreach (var entry in categoryEntries)
        {
            var cat = entry.Entity;
            var action = entry.State == EntityState.Added ? "added" : "modified";

            var payload = new CategoryPayload(cat.UserId,
                cat.Id,
                cat.Name,
                cat.TypeId,
                cat.ParentId,
                cat.IsDeleted,
                action);

            db.OutboxEvents.Add(new()
            {
                EventType = OutboxEvent.CategoryType,
                Payload = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow,
            });
        }

        return new(result);
    }
}
