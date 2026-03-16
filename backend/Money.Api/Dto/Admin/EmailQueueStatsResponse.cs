namespace Money.Api.Dto.Admin;

public class EmailQueueStatsResponse
{
    public long QueueLength { get; init; }
    public long RetryLength { get; init; }
    public long DlqLength { get; init; }
    public List<EmailPreview> RecentMessages { get; init; } = [];
    public List<EmailPreview> RetryMessages { get; init; } = [];
    public List<EmailPreview> DlqMessages { get; init; } = [];
}

public class EmailPreview
{
    public Guid Id { get; init; }
    public string ReceiverEmail { get; init; } = "";
    public string Title { get; init; } = "";
    public int RetryCount { get; init; }
    public DateTimeOffset EnqueuedAt { get; init; }
    public DateTimeOffset? NextRetryAt { get; init; }
}
