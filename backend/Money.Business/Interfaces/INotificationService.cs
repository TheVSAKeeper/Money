namespace Money.Business.Interfaces;

public interface INotificationService
{
    Task PublishAsync(int userId, string shardName, string eventType, object payload);
}
