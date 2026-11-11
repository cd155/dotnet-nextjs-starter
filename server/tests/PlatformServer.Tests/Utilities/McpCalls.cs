using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PlatformServer.Tests.Utilities;

/// <summary>JSON-RPC requests to <c>/mcp</c>, for tests that go through <see cref="AppApiFactory"/>.</summary>
public static class McpCalls
{
    /// <summary>A client whose requests carry the access token.</summary>
    public static HttpClient WithToken(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>The names of the tools the client is shown, in order.</summary>
    public static async Task<string[]> ListToolsAsync(HttpClient client)
    {
        var result = await CallAsync(client, "tools/list", new { });

        return result.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Calls a tool and returns its <c>result</c>: its text is what the tool returned, as JSON, and <c>isError</c> is
    /// true when it failed, with the message as its text.
    /// </summary>
    public static Task<JsonElement> CallToolAsync(HttpClient client, string name, object arguments) =>
        CallAsync(client, "tools/call", new { name, arguments });

    /// <summary>Calls a tool that must succeed, and returns what it returned.</summary>
    public static async Task<JsonElement> CallToolOkAsync(HttpClient client, string name, object arguments)
    {
        var result = await CallToolAsync(client, name, arguments);
        Assert.False(IsError(result), TextOf(result));
        return JsonDocument.Parse(TextOf(result)).RootElement.Clone();
    }

    /// <summary>Whether a tool call's result is a failure.</summary>
    public static bool IsError(JsonElement result) =>
        result.TryGetProperty("isError", out var isError) && isError.GetBoolean();

    /// <summary>A tool call's text: its message when it failed.</summary>
    public static string TextOf(JsonElement result) =>
        string.Concat(result.GetProperty("content").EnumerateArray().Select(c => c.GetProperty("text").GetString()));

    /// <summary>
    /// Sends one JSON-RPC request to /mcp and returns its <c>result</c>, failing on a JSON-RPC error. The endpoint is
    /// stateless, so a request needs no initialize first. The answer may come as a server-sent event, whose
    /// <c>data:</c> line is the JSON.
    /// </summary>
    public static async Task<JsonElement> CallAsync(HttpClient client, string method, object parameters)
    {
        var message = await SendAsync(client, method, parameters);
        Assert.False(message.TryGetProperty("error", out var error), error.ToString());

        return message.GetProperty("result").Clone();
    }

    /// <summary>Sends one JSON-RPC request to /mcp and returns the whole answer, a result or an error.</summary>
    public static async Task<JsonElement> SendAsync(HttpClient client, string method, object parameters)
    {
        using var response = await PostAsync(client, method, parameters);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");

        var json = body.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("data:", StringComparison.Ordinal))?["data:".Length..]
            ?? body;

        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>Posts one JSON-RPC request to /mcp and returns the HTTP response, whatever its status.</summary>
    public static async Task<HttpResponseMessage> PostAsync(HttpClient client, string method, object parameters)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        return await client.SendAsync(request);
    }
}
