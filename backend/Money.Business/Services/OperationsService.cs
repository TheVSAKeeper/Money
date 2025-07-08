using Money.Data.Extensions;

namespace Money.Business.Services;

public class OperationsService(
    RequestEnvironment environment,
    ApplicationDbContext context,
    UsersService usersService,
    CategoriesService categoriesService,
    PlacesService placesService,
    BusinessObservabilityService observabilityService)
{
    public async Task<IEnumerable<Operation>> GetAsync(OperationFilter filter, CancellationToken cancellationToken = default)
    {
        var filteredOperations = FilterOperations(filter);

        var placeIds = await filteredOperations
            .Where(x => x.PlaceId != null)
            .Select(x => x.PlaceId!.Value)
            .ToListAsync(cancellationToken);

        var places = await placesService.GetPlacesAsync(placeIds, cancellationToken);

        var models = await filteredOperations
            .OrderByDescending(x => x.Date)
            .ThenBy(x => x.CategoryId)
            .ToListAsync(cancellationToken);

        return models.Select(x => GetBusinessModel(x, places)).ToList();
    }

    public async Task<Operation> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entity = await GetByIdInternal(id, cancellationToken);

        List<Place>? places = null;

        if (entity.PlaceId != null)
        {
            places = await placesService.GetPlacesAsync([entity.PlaceId.Value], cancellationToken);
        }

        return GetBusinessModel(entity, places);
    }

    public async Task<int> CreateAsync(Operation model, CancellationToken cancellationToken = default)
    {
        using var businessSpan = observabilityService.StartEnhancedBusinessSpan("create",
            "operation",
            userId: environment.UserId,
            additionalTags: new()
            {
                ["amount"] = model.Sum,
                ["category_id"] = model.CategoryId,
                ["place"] = model.Place ?? "unknown",
                ["date"] = model.Date.ToString("yyyy-MM-dd"),
            });

        try
        {
            observabilityService.AddBusinessOperationStartEvent("operation_create", new()
            {
                ["amount"] = model.Sum,
                ["category_id"] = model.CategoryId,
                ["user_id"] = environment.UserId,
            });

            observabilityService.EnrichAutomaticSpans("operation_create_started", new()
            {
                ["entity_type"] = "operation",
                ["operation_type"] = "create",
                ["user_id"] = environment.UserId,
            });

            using (observabilityService.StartNestedBusinessSpan("validation", "operation"))
            {
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "ValidationStart");

                Validate(model);

                observabilityService.AddValidationEvent("operation", true);
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "ValidationCompleted");
            }

            Category category;

            using (var categorySpan = observabilityService.StartNestedBusinessSpan("category_lookup", "operation"))
            {
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "CategoryLookup");

                category = await categoriesService.GetByIdAsync(model.CategoryId, cancellationToken);

                categorySpan?.SetTag("category.id", category.Id);
                categorySpan?.SetTag("category.name", category.Name);
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "CategoryLookupCompleted");
            }

            int operationId;

            using (var idGenerationSpan = observabilityService.StartNestedBusinessSpan("id_generation", "operation"))
            {
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "GenerateOperationId");

                operationId = await usersService.GetNextOperationIdAsync(cancellationToken);

                idGenerationSpan?.SetTag("generated.operation_id", operationId);
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "OperationIdGenerated");
            }

            int? placeId;

            using (var placeSpan = observabilityService.StartNestedBusinessSpan("place_lookup", "operation"))
            {
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "PlaceLookup");

                placeId = model.PlaceId ?? await placesService.GetPlaceIdAsync(model.Place, cancellationToken);

                if (placeId.HasValue)
                {
                    placeSpan?.SetTag("place.id", placeId.Value);
                }

                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "PlaceLookupCompleted");
            }

            var entity = new Data.Entities.Operation
            {
                Id = operationId,
                UserId = environment.UserId,
                CategoryId = category.Id,
                Sum = model.Sum,
                Comment = model.Comment,
                Date = model.Date,
                PlaceId = placeId,
                CreatedTaskId = model.CreatedTaskId,
            };

            using (observabilityService.StartNestedBusinessSpan("database_insert", "operation"))
            {
                observabilityService.AddEventToBothCurrentAndSpan(businessSpan, "DatabaseInsert");

                await context.Operations.AddAsync(entity, cancellationToken);
                await context.SaveChangesAsync(cancellationToken);

                observabilityService.AddDatabaseEvent("INSERT", "operations", 1);

                observabilityService.AddEventToSpan(businessSpan, "DatabaseInsertCompleted", new()
                {
                    ["operation_id"] = operationId,
                    ["records_affected"] = 1,
                });
            }

            businessSpan?.SetTag("operation.id", operationId);

            observabilityService.AddBusinessOperationEndEvent("operation_create", true, new()
            {
                ["operation_id"] = operationId,
            });

            observabilityService.AddBusinessOperationEventToSpan(businessSpan, "operation_create", true, new()
            {
                ["operation_id"] = operationId,
                ["processing_duration_ms"] = businessSpan?.Duration.TotalMilliseconds ?? 0,
            });

            return operationId;
        }
        catch (Exception ex)
        {
            observabilityService.RecordException(ex);

            observabilityService.AddBusinessOperationEndEvent("operation_create", false, new()
            {
                ["error_type"] = ex.GetType().Name,
            });

            observabilityService.AddBusinessOperationEventToSpan(businessSpan, "operation_create", false, new()
            {
                ["error_type"] = ex.GetType().Name,
                ["error_message"] = ex.Message,
            });

            throw;
        }
    }

    public async Task UpdateAsync(Operation model, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var entity = await context.Operations.FirstOrDefaultAsync(environment.UserId, model.Id, cancellationToken)
                     ?? throw new NotFoundException("операция", model.Id);

        var category = await categoriesService.GetByIdAsync(model.CategoryId, cancellationToken);
        var placeId = await placesService.GetPlaceIdAsync(model.Place, entity, cancellationToken);

        entity.Sum = model.Sum;
        entity.Comment = model.Comment;
        entity.Date = model.Date;
        entity.CategoryId = category.Id;
        entity.PlaceId = placeId;

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
        var entity = await context.Operations
                         .IgnoreQueryFilters()
                         .Where(x => x.IsDeleted)
                         .FirstOrDefaultAsync(environment.UserId, id, cancellationToken)
                     ?? throw new NotFoundException("операция", id);

        entity.IsDeleted = false;
        await placesService.CheckRestorePlaceAsync(entity.PlaceId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<Operation>> UpdateBatchAsync(List<int> operationIds, int categoryId, CancellationToken cancellationToken = default)
    {
        var category = await categoriesService.GetByIdAsync(categoryId, cancellationToken);

        var entities = await context.Operations
            .IsUserEntity(environment.UserId)
            .Where(x => operationIds.Contains(x.Id))
            .Include(x => x.Category)
            .ToListAsync(cancellationToken);

        if (entities.Count != operationIds.Count)
        {
            throw new BusinessException("Одна или несколько операций не найдены");
        }

        foreach (var model in entities)
        {
            model.CategoryId = category.Id;

            if (model.Category?.TypeId != (int)category.OperationType)
            {
                model.Sum = -model.Sum;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return entities.Select(x => GetBusinessModel(x)).ToList();
    }

    private static Operation GetBusinessModel(Data.Entities.Operation model, IEnumerable<Place>? dbPlaces = null)
    {
        return new()
        {
            CategoryId = model.CategoryId,
            Sum = model.Sum,
            Comment = model.Comment,
            Place = model.PlaceId.HasValue
                ? dbPlaces?.FirstOrDefault(x => x.Id == model.PlaceId)?.Name
                : null,
            Id = model.Id,
            Date = model.Date,
            CreatedTaskId = model.CreatedTaskId,
        };
    }

    private static void Validate(Operation model)
    {
        if (model.Comment?.Length > 4000)
        {
            throw new BusinessException("Извините, но комментарий слишком длинный");
        }

        if (model.Place?.Length > 500)
        {
            throw new BusinessException("Извините, но название места слишком длинное");
        }

        if (model.Date == default)
        {
            throw new BusinessException("Извините, дата обязательна");
        }
    }

    private async Task<Data.Entities.Operation> GetByIdInternal(int id, CancellationToken cancellationToken = default)
    {
        var dbCategory = await context.Operations
                             .IsUserEntity(environment.UserId, id)
                             .FirstOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("операция", id);

        return dbCategory;
    }

    private IQueryable<Data.Entities.Operation> FilterOperations(OperationFilter filter)
    {
        var entities = context.Operations
            .IsUserEntity(environment.UserId);

        if (filter.DateFrom.HasValue)
        {
            entities = entities.Where(x => x.Date >= filter.DateFrom.Value);
        }

        if (filter.DateTo.HasValue)
        {
            entities = entities.Where(x => x.Date < filter.DateTo.Value);
        }

        if (filter.CategoryIds is { Count: > 0 })
        {
            entities = entities.Where(x => filter.CategoryIds.Contains(x.CategoryId));
        }

        if (string.IsNullOrEmpty(filter.Comment) == false)
        {
            entities = entities.Where(x => x.Comment != null && x.Comment.Contains(filter.Comment));
        }

        if (string.IsNullOrEmpty(filter.Place) == false)
        {
            var placesIds = context.Places
                .Where(x => x.UserId == environment.UserId && x.Name.Contains(filter.Place))
                .Select(x => x.Id);

            entities = entities.Where(x => x.PlaceId != null && placesIds.Contains(x.PlaceId.Value));
        }

        return entities;
    }
}
