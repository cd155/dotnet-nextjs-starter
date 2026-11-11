using PlatformServer.Features.Email;

namespace PlatformServer.Tests.Utilities;

/// <summary>
/// Stands in for <see cref="EmailQueue"/> in <see cref="AppApiFactory"/>: sends at once, so a test sees the
/// email as soon as its request returns.
/// </summary>
public sealed class InlineEmailQueue : IEmailQueue
{
    // The fake sender finishes synchronously, so this doesn't block.
    public void Enqueue(Func<Task> send) => send().GetAwaiter().GetResult();
}
