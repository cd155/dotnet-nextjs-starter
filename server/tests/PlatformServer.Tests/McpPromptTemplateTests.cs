using PlatformServer.Features.Mcp.Prompts;
using Xunit;

namespace PlatformServer.Tests;

// The MCP prompts' embedded templates. What each prompt fills them with is tested through /mcp in McpNoteToolsTests.
public class McpPromptTemplateTests
{
    [Theory]
    [InlineData("notes-summary", "--- NOTES ---\nVALUE-notes")]
    public void EachTemplate_RendersWithItsPlaceholders(string name, string expected)
    {
        var prompt = McpPromptTemplate.Render(name, new Dictionary<string, string> { ["notes"] = "VALUE-notes" });

        Assert.Contains(expected, prompt);
        Assert.DoesNotContain("{{", prompt);
        Assert.Equal(prompt.TrimEnd(), prompt);
    }

    [Fact]
    public void Render_ForAMissingTemplate_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            McpPromptTemplate.Render("no-such-prompt", new Dictionary<string, string>()));

        Assert.Contains("no-such-prompt", exception.Message);
    }

    [Fact]
    public void Fill_SubstitutesValues_AndIgnoresExtras()
    {
        var result = McpPromptTemplate.Fill(
            "Read {{name}} and {{ name }}.",
            new Dictionary<string, string> { ["name"] = "this", ["unused"] = "x" });

        Assert.Equal("Read this and this.", result);
    }

    [Fact]
    public void Fill_Throws_WhenAPlaceholderHasNoValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            McpPromptTemplate.Fill("Read {{name}}.", new Dictionary<string, string>()));

        Assert.Contains("name", exception.Message);
    }
}
