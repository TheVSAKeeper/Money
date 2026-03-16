namespace Money.Business.Interfaces;

public interface IEmailQueueService
{
    Task EnqueueAsync(MailMessage message);
    Task<List<MailEnvelope>> DequeueBatchAsync(int count);
    Task EnqueueRetryAsync(MailEnvelope envelope);
    Task EnqueueDeadLetterAsync(MailEnvelope envelope);
    Task<List<MailEnvelope>> DequeueReadyRetriesAsync(int count);
    Task<long> GetQueueLengthAsync();
    Task<long> GetRetryQueueLengthAsync();
    Task<long> GetDeadLetterQueueLengthAsync();
    Task<List<MailEnvelope>> PeekQueueAsync(int count);
    Task<List<MailEnvelope>> PeekRetryQueueAsync(int count);
    Task<List<MailEnvelope>> PeekDeadLetterQueueAsync(int count);
}
