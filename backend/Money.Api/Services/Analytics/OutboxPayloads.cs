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
    string? Comment);

internal record DebtPayload(
    int UserId,
    int DebtId,
    string OwnerName,
    int TypeId,
    decimal Sum,
    decimal PaySum,
    int StatusId,
    DateOnly Date);
