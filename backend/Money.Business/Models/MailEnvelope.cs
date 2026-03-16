namespace Money.Business.Models;

public class MailEnvelope
{
    public required MailMessage Message { get; init; }
    public int RetryCount { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}
