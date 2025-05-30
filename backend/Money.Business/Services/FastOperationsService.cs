﻿using Money.Data.Extensions;

namespace Money.Business.Services;

public class FastOperationsService(
    RequestEnvironment environment,
    ApplicationDbContext context,
    UsersService usersService,
    CategoriesService categoriesService,
    PlacesService placesService)
{
    public async Task<IEnumerable<FastOperation>> GetAsync(CancellationToken cancellationToken = default)
    {
        var entities = context.FastOperations
            .IsUserEntity(environment.UserId);

        var placeIds = await entities
            .Where(x => x.PlaceId != null)
            .Select(x => x.PlaceId!.Value)
            .ToListAsync(cancellationToken);

        var places = await placesService.GetPlacesAsync(placeIds, cancellationToken);

        var models = await entities
            .OrderBy(x => x.CategoryId)
            .ToListAsync(cancellationToken);

        return models.Select(x => GetBusinessModel(x, places)).ToList();
    }

    public async Task<FastOperation> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdInternal(id, cancellationToken);

        List<Place>? places = null;

        if (entity.PlaceId != null)
        {
            places = await placesService.GetPlacesAsync([entity.PlaceId.Value], cancellationToken);
        }

        return GetBusinessModel(entity, places);
    }

    public async Task<int> CreateAsync(FastOperation model, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var category = await categoriesService.GetByIdAsync(model.CategoryId, cancellationToken);
        var operationId = await usersService.GetNextFastOperationIdAsync(cancellationToken);
        var placeId = await placesService.GetPlaceIdAsync(model.Place, cancellationToken);

        var entity = new Data.Entities.FastOperation
        {
            Id = operationId,
            Name = model.Name,
            UserId = environment.UserId,
            CategoryId = category.Id,
            Sum = model.Sum,
            Comment = model.Comment,
            PlaceId = placeId,
            Order = model.Order,
        };

        await context.FastOperations.AddAsync(entity, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return operationId;
    }

    public async Task UpdateAsync(FastOperation model, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var entity = await context.FastOperations
                         .IsUserEntity(environment.UserId, model.Id)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException("Быстрая операция", model.Id);

        var category = await categoriesService.GetByIdAsync(model.CategoryId, cancellationToken);
        var id = await placesService.GetPlaceIdAsync(model.Place, entity, cancellationToken);

        entity.Sum = model.Sum;
        entity.Comment = model.Comment;
        entity.CategoryId = category.Id;
        entity.PlaceId = id;
        entity.Order = model.Order;
        entity.Name = model.Name;

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdInternal(id, cancellationToken);
        entity.IsDeleted = true;
        await placesService.CheckRemovePlaceAsync(entity.PlaceId, entity.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.FastOperations
                         .IgnoreQueryFilters()
                         .Where(x => x.IsDeleted)
                         .IsUserEntity(environment.UserId, id)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException("Быстрая операция", id);

        entity.IsDeleted = false;
        await placesService.CheckRestorePlaceAsync(entity.PlaceId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static FastOperation GetBusinessModel(Data.Entities.FastOperation entity, IEnumerable<Place>? dbPlaces = null)
    {
        return new()
        {
            CategoryId = entity.CategoryId,
            Sum = entity.Sum,
            Comment = entity.Comment,
            Place = entity.PlaceId.HasValue
                ? dbPlaces?.FirstOrDefault(x => x.Id == entity.PlaceId)?.Name
                : null,
            Id = entity.Id,
            Name = entity.Name,
            Order = entity.Order,
        };
    }

    private static void Validate(FastOperation model)
    {
        if (model.Comment?.Length > 4000)
        {
            throw new BusinessException("Извините, но комментарий слишком длинный");
        }

        if (model.Place?.Length > 500)
        {
            throw new BusinessException("Извините, но название места слишком длинное");
        }

        if (string.IsNullOrWhiteSpace(model.Name))
        {
            throw new BusinessException("Извините, но имя обязательно");
        }

        if (model.Name.Length > 500)
        {
            throw new BusinessException("Извините, но имя слишком длинное");
        }
    }

    private async Task<Data.Entities.FastOperation> GetByIdInternal(int id, CancellationToken cancellationToken = default)
    {
        var entity = await context.FastOperations
                         .IsUserEntity(environment.UserId, id)
                         .FirstOrDefaultAsync(cancellationToken)
                     ?? throw new NotFoundException("Быстрая операция", id);

        return entity;
    }
}
