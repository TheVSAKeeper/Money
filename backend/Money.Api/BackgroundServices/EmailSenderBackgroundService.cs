using Microsoft.Extensions.Options;
using Money.Api.Services.Notifications;
using Money.Business.Interfaces;
using Money.Common;

namespace Money.Api.BackgroundServices;

public class EmailSenderBackgroundService(
    IEmailQueueService emailQueueService,
    IMailsService mailsService,
    AdminNotificationPublisher adminPublisher,
    ILogger<EmailSenderBackgroundService> logger,
    IOptions<EmailSenderSettings> options) : BackgroundService
{
    private readonly EmailSenderSettings _settings = options.Value;
    private readonly SemaphoreSlim _semaphore = new(Environment.ProcessorCount * 2);
    private PeriodicTimer _timer = null!;

    public override void Dispose()
    {
        _timer?.Dispose();
        _semaphore.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new(_settings.ProcessingInterval);

        logger.LogInformation("{ServiceName} запущен с интервалом {Interval}", nameof(EmailSenderBackgroundService), _settings.ProcessingInterval);
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("{Name} останавливается", nameof(EmailSenderBackgroundService));
        return base.StopAsync(cancellationToken);
    }

    public async Task ForceExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Принудительный запуск обработки очереди");

            using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedSource.CancelAfter(_settings.ProcessingInterval);
            await ProcessTickAsync(linkedSource.Token);

            logger.LogInformation("Принудительная обработка завершена");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Принудительная обработка прервана");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при принудительной обработке");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            await ProcessTickAsync(stoppingToken);
        } while (await _timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        await RequeueReadyRetriesAsync(cancellationToken);
        await ProcessMessagesAsync(cancellationToken);
    }

    private async Task RequeueReadyRetriesAsync(CancellationToken cancellationToken)
    {
        var readyRetries = await emailQueueService.DequeueReadyRetriesAsync(100);

        foreach (var envelope in readyRetries)
        {
            await emailQueueService.EnqueueAsync(envelope.Message);
        }

        if (readyRetries.Count > 0)
        {
            logger.LogDebug("Повторно поставлено в очередь {Count} сообщений из retry", readyRetries.Count);
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        var envelopes = await emailQueueService.DequeueBatchAsync(_settings.MaxBatchSize);

        if (envelopes.Count == 0)
        {
            return;
        }

        var tasks = envelopes.Select(e => ProcessSingleEnvelopeAsync(e, cancellationToken));
        await Task.WhenAll(tasks);
        logger.LogDebug("Обработано {BatchSize} сообщений", envelopes.Count);
    }

    private async Task ProcessSingleEnvelopeAsync(MailEnvelope envelope, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            var stopwatch = ValueStopwatch.StartNew();
            await mailsService.SendAsync(envelope.Message, cancellationToken);

            logger.LogInformation("Сообщение {MessageId} отправлено за {Elapsed} мс",
                envelope.Message.Id, stopwatch.GetElapsedTime().TotalMilliseconds);

            await adminPublisher.PublishEmailQueueChangedAsync(await emailQueueService.GetQueueLengthAsync(),
                await emailQueueService.GetRetryQueueLengthAsync(),
                await emailQueueService.GetDeadLetterQueueLengthAsync(),
                "EmailSent");
        }
        catch (OperationCanceledException)
        {
            await emailQueueService.EnqueueAsync(envelope.Message);
            logger.LogWarning("Операция отменена, сообщение {MessageId} возвращено в очередь", envelope.Message.Id);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Критическая ошибка при обработке сообщения {MessageId}", envelope.Message.Id);
            await HandleRetryAsync(envelope, exception);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task HandleRetryAsync(MailEnvelope envelope, Exception exception)
    {
        if (envelope.RetryCount >= _settings.MaxRetries)
        {
            logger.LogError(exception, "Достигнут максимум повторов для сообщения {MessageId}", envelope.Message.Id);
            await emailQueueService.EnqueueDeadLetterAsync(envelope);

            await adminPublisher.PublishEmailQueueChangedAsync(await emailQueueService.GetQueueLengthAsync(),
                await emailQueueService.GetRetryQueueLengthAsync(),
                await emailQueueService.GetDeadLetterQueueLengthAsync(),
                "EmailDead");

            return;
        }

        envelope.RetryCount++;

        logger.LogWarning(exception, "Повторная попытка {RetryCount} для сообщения {MessageId}",
            envelope.RetryCount, envelope.Message.Id);

        await emailQueueService.EnqueueRetryAsync(envelope);

        await adminPublisher.PublishEmailQueueChangedAsync(await emailQueueService.GetQueueLengthAsync(),
            await emailQueueService.GetRetryQueueLengthAsync(),
            await emailQueueService.GetDeadLetterQueueLengthAsync(),
            "EmailRetried");
    }
}
