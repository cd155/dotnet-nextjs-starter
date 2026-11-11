using Microsoft.AspNetCore.Hosting;

namespace PlatformServer.Tests.Utilities;

/// <summary>
/// One <see cref="AppApiFactory"/> for all of a test class's tests, as an <c>IClassFixture</c>: xUnit runs a
/// class's tests one at a time, so each test can reset it and have the app to itself.
/// </summary>
public sealed class SharedApiFactory : IAsyncDisposable
{
    private AppApiFactory? _factory;

    /// <summary>The class's factory, reset for this test.</summary>
    /// <param name="create">
    /// Creates the factory on the class's first call; later calls reuse it, so every test of the class must pass the
    /// same settings.
    /// </param>
    public async Task<AppApiFactory> ResetAsync(Func<AppApiFactory>? create = null)
    {
        _factory ??= create?.Invoke() ?? new AppApiFactory();
        await _factory.ResetAsync();
        return _factory;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }
}
