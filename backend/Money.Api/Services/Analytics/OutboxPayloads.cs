namespace Money.Api.Services.Analytics;

internal record OperationPayload(
    int UserId,
    Guid AuthUserId,
    int OperationId,
    int CategoryId,
    string CategoryName,
    int OperationType,
    decimal Sum,
    DateOnly Date,
    string? PlaceName,
    string? Comment,
    string Action = "modified");

internal record DebtPayload(
    int UserId,
    Guid AuthUserId,
    int DebtId,
    string OwnerName,
    int TypeId,
    decimal Sum,
    decimal PaySum,
    int StatusId,
    DateOnly Date,
    bool IsDeleted = false,
    string Action = "modified");

internal record CategoryPayload(
    int UserId,
    Guid AuthUserId,
    int CategoryId,
    string Name,
    int TypeId,
    int? ParentId,
    bool IsDeleted = false,
    string Action = "modified");
