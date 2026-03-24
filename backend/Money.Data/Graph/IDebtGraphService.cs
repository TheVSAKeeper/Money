namespace Money.Data.Graph;

public interface IDebtGraphService
{
    Task SyncDebtAsync(string userId, int debtId, string ownerName, decimal amount, string status, int type);
    Task SyncPaymentAsync(string userId, int debtId, decimal paySum, string newStatus);
    Task ForgiveDebtAsync(string userId, int debtId);
    Task DeleteDebtAsync(string userId, int debtId);
    Task MergeOwnersAsync(string userId, string fromName, string toName);
    Task<GraphDto> GetDebtNetworkAsync(string userId, int limit = 200);
}
