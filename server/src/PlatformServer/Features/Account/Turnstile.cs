using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PlatformServer.Features.Account;

/// <summary>Cloudflare Turnstile, from the <c>Turnstile</c> section: the check that a person, not a script, filled in a form.</summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>The widget's secret key. Left blank in Development, every token passes.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Whether tokens pass unchecked: only in Development, with no secret key.</summary>
    public bool SkipsCheck(IHostEnvironment environment) =>
        environment.IsDevelopment() && string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>A request that carries the token the web app's Turnstile widget gave the user.</summary>
public interface ITurnstileProtected
{
    string? TurnstileToken { get; }
}

/// <summary>
/// Checks a token with Cloudflare's siteverify. A token works once and lasts five minutes. The widget may serve other
/// hosts too, so the token's hostname must be the web app's, from <c>WebApp:BaseUrl</c>.
/// </summary>
/// <remarks>
/// Fails closed: without a secret key outside Development, or when Cloudflare can't be reached, no token passes, so
/// sign-up stops rather than sending email to anyone a script names.
/// </remarks>
public sealed class TurnstileVerifier(
    HttpClient http,
    IOptions<TurnstileOptions> options,
    IOptions<WebAppOptions> webApp,
    IHostEnvironment environment,
    ILogger<TurnstileVerifier> logger)
{
    public static readonly Uri SiteVerifyUrl = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    /// <summary>Whether Cloudflare accepts the token, made for the web app's host.</summary>
    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken)
    {
        if (options.Value.SkipsCheck(environment))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(options.Value.SecretKey) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = options.Value.SecretKey, ["response"] = token };
        if (remoteIp is not null)
        {
            form["remoteip"] = remoteIp;
        }

        SiteVerifyResponse? result;
        try
        {
            using var response = await http.PostAsync(SiteVerifyUrl, new FormUrlEncodedContent(form), cancellationToken);
            response.EnsureSuccessStatusCode();
            result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogError(ex, "Couldn't check a Turnstile token with Cloudflare");
            return false;
        }

        if (result is not { Success: true })
        {
            logger.LogInformation("Turnstile refused a token: {Errors}", string.Join(", ", result?.ErrorCodes ?? []));
            return false;
        }

        if (!Uri.TryCreate(webApp.Value.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            logger.LogError("WebApp:BaseUrl isn't an absolute URL, so no Turnstile token can be checked against its host");
            return false;
        }

        var expectedHost = baseUrl.Host;
        if (!string.Equals(result.Hostname, expectedHost, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Turnstile token was made for {Hostname}, not {Expected}", result.Hostname, expectedHost);
            return false;
        }

        return true;
    }

    private sealed record SiteVerifyResponse(
        bool Success,
        string? Hostname,
        [property: JsonPropertyName("error-codes")] IReadOnlyList<string>? ErrorCodes);
}

/// <summary>Refuses a request whose Turnstile token doesn't pass, before the endpoint runs.</summary>
public sealed class TurnstileFilter(TurnstileVerifier verifier) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<ITurnstileProtected>().Single();
        var http = context.HttpContext;

        if (!await verifier.VerifyAsync(request.TurnstileToken, http.Connection.RemoteIpAddress?.ToString(), http.RequestAborted))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["turnstileToken"] = ["The check that you're not a bot didn't pass. Please try again."]
            });
        }

        return await next(context);
    }
}

public static class TurnstileEndpointExtensions
{
    /// <summary>Requires a passing Turnstile token in the request body, which implements <see cref="ITurnstileProtected"/>.</summary>
    public static RouteHandlerBuilder RequireTurnstile(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<TurnstileFilter>().ProducesValidationProblem();
}
