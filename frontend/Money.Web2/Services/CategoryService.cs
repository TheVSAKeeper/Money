﻿using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Memory;
using Money.ApiClient;
using Money.Web2.Models;

namespace Money.Web2.Services;

public class CategoryService(MoneyClient moneyClient, IMemoryCache memoryCache, ILogger<CategoryService> logger)
{
    private const string CacheKey = nameof(CategoryService);
    private readonly TimeSpan _cacheLifetime = TimeSpan.FromMinutes(5);

    public async Task<List<Category>> GetAllAsync()
    {
        if (memoryCache.TryGetValue(CacheKey, out List<Category>? categories))
        {
            return categories ?? [];
        }

        ApiClientResponse<CategoryClient.Category[]> apiCategories = await moneyClient.Category.Get();

        categories = apiCategories.Content?
            .Select(apiCategory => new Category
            {
                Id = apiCategory.Id,
                ParentId = apiCategory.ParentId,
                Name = apiCategory.Name,
                Color = apiCategory.Color,
                Order = apiCategory.Order,
                OperationType = OperationTypes.Values.FirstOrDefault(x => x.Id == apiCategory.OperationTypeId) ?? OperationTypes.None,
            })
            .ToList();

        if (categories == null)
        {
            return [];
        }

        categories = memoryCache.Set(CacheKey, categories, _cacheLifetime);
        return categories;
    }

    public async Task<Result> SaveAsync(Category category)
    {
        try
        {
            CategoryClient.SaveRequest saveRequest = new()
            {
                Name = category.Name,
                Order = category.Order,
                Color = category.Color,
                ParentId = category.ParentId,
                OperationTypeId = category.OperationType.Id,
            };

            if (category.Id == null)
            {
                ApiClientResponse<int> result = await moneyClient.Category.Create(saveRequest);
                category.Id = result.Content;
            }
            else
            {
                await moneyClient.Category.Update(category.Id.Value, saveRequest);
            }

            InvalidateCacheAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка при сохранении категории");
            return Result.Failure("Не удалось сохранить категорию. Пожалуйста, попробуйте еще раз.");
        }

        return Result.Success();
    }

    private void InvalidateCacheAsync()
    {
        memoryCache.Remove(CacheKey);
    }
}
