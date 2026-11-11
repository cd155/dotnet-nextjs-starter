using System.Threading.Channels;

namespace PlatformServer.Features.Email;

/// <summary>
/// Emails sent after the request that asks for them. A slow SMTP server can take longer than the web app
/// waits for an answer, so the request would fail while the email still went out. The tests' <c>InlineEmailQueue</c>
/// sends at once instead.
/// </summary>
public interface IEmailQueue
{
    /// <summary>Queues a send, which logs its own failure: nothing waits for it.</summary>
    void Enqueue(Func<Task> send);
}

/// <summary>
/// The sends waiting for <see cref="EmailQueueWorker"/>. One still queued when the app stops is lost, and logged.
/// </summary>
public sealed class EmailQueue : IEmailQueue
{
    // Not SingleReader: that channel can't count what's left.
    private readonly Channel<Func<Task>> _sends = Channel.CreateUnbounded<Func<Task>>();

    public void Enqueue(Func<Task> send) => _sends.Writer.TryWrite(send);

    /// <summary>The sends still waiting.</summary>
    public int Count => _sends.Reader.Count;

    public IAsyncEnumerable<Func<Task>> ReadAllAsync(CancellationToken cancellationToken) =>
        _sends.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>Sends the queued emails one at a time, so a slow send delays the next email but no request.</summary>
public sealed class EmailQueueWorker(EmailQueue queue, ILogger<EmailQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var send in queue.ReadAllAsync(stoppingToken))
        {
            // The sends log their own failures; this keeps one that doesn't from stopping the queue.
            try
            {
                await send();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A queued email failed");
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // A send that never ran logs nothing itself, so the lost ones are logged here.
        if (queue.Count > 0)
        {
            logger.LogWarning("{Count} queued emails weren't sent before the app stopped", queue.Count);
        }
    }
}
