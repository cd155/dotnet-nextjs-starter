using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace PlatformServer.Features.Mcp.Prompts;

/// <summary>
/// The MCP prompts' templates: Markdown files beside this class, embedded in the assembly, each with
/// <c>{{name}}</c> placeholders that <see cref="NotePrompts"/> fills. A template ships with the code that fills
/// it, so a placeholder the code doesn't supply is a bug, and <c>McpPromptTemplateTests</c> renders every one.
/// </summary>
public static partial class McpPromptTemplate
{
    private static readonly ConcurrentDictionary<string, string> Templates = new();

    [GeneratedRegex(@"{{\s*([A-Za-z0-9_]+)\s*}}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>The template <c>{name}.md</c> with each <c>{{placeholder}}</c> replaced by its value; extra values are ignored.</summary>
    /// <exception cref="InvalidOperationException">The template is missing, or names a placeholder with no value.</exception>
    public static string Render(string name, IReadOnlyDictionary<string, string> values) =>
        Fill(Templates.GetOrAdd(name, Read), values);

    /// <summary>Fills <paramref name="template"/>'s placeholders, as <see cref="Render"/> does.</summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        PlaceholderRegex().Replace(template, match =>
            values.TryGetValue(match.Groups[1].Value, out var value)
                ? value
                : throw new InvalidOperationException($"No value for the placeholder '{match.Groups[1].Value}'."));

    private static string Read(string name)
    {
        var resource = $"PlatformServer.Features.Mcp.Prompts.{name}.md";
        using var stream = typeof(McpPromptTemplate).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource {resource} is missing.");
        using var reader = new StreamReader(stream);
        // The file's final newline isn't part of the prompt.
        return reader.ReadToEnd().TrimEnd();
    }
}
