namespace Money.Data.Graph;

public interface ICategoryGraphService
{
    Task SyncCategoryAsync(string userId, int categoryId, string name, int typeId, int? parentId);
    Task DeleteCategoryAsync(string userId, int categoryId);
    Task UpdateOperationFlowAsync(string userId, int categoryId, string yearMonth, long sumDeltaCents, int countDelta);
    Task<GraphDto> GetCategoryTreeAsync(string userId, int limit = 500);
}
