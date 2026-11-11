using Microsoft.Extensions.Logging;
using PlatformServer.Features.Email;
using PlatformServer.Tests.Utilities;
using Xunit;

namespace PlatformServer.Tests;

// The worker that sends the account's emails after the request. The API tests send inline instead.
public class EmailQueueTests
{
    [Fact]
    public async Task Worker_SendsQueuedEmails_AndKeepsGoingAfterAFailure()
    {
        var logs = new LogCapture();
        var queue = new EmailQueue();
        using var worker = new EmailQueueWorker(queue, logs.CreateLogger<EmailQueueWorker>());
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        queue.Enqueue(() => throw new InvalidOperationException("SMTP is down."));
        queue.Enqueue(() =>
        {
            sent.SetResult();
            return Task.CompletedTask;
        });
        await worker.StartAsync(CancellationToken.None);

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        var failure = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Error);
        Assert.True(failure.Mentions("SMTP is down."));
    }

    [Fact]
    public async Task Worker_LogsTheEmailsStillQueued_WhenTheAppStops()
    {
        var logs = new LogCapture();
        var queue = new EmailQueue();
        using var worker = new EmailQueueWorker(queue, logs.CreateLogger<EmailQueueWorker>());

        queue.Enqueue(() => Task.CompletedTask);
        await worker.StopAsync(CancellationToken.None);

        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.True(warning.Mentions("1 queued emails"));
    }
}
