namespace Money.Api.Services.Analytics;

internal record OperationPayload(
    int UserId,
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
    int CategoryId,
    string Name,
    int TypeId,
    int? ParentId,
    bool IsDeleted = false,
    string Action = "modified");
