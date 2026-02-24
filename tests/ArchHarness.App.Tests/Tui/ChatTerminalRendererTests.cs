using ArchHarness.App.Tui;

namespace ArchHarness.App.Tests.Tui;

/// <summary>
/// Verifies ChatTerminalRenderer.SummarizePrompt truncation logic.
/// </summary>
public class ChatTerminalRendererTests
{
    /// <summary>
    /// Short inputs should be returned unchanged.
    /// </summary>
    [Fact]
    public void SummarizePrompt_ShortInput_ReturnsUnchanged()
    {
        string input = "This is a short prompt.";

        string result = ChatTerminalRenderer.SummarizePrompt(input);

        Assert.Equal(input, result);
    }

    /// <summary>
    /// An input exactly at the 120-character limit should not be truncated.
    /// </summary>
    [Fact]
    public void SummarizePrompt_ExactlyAtLimit_ReturnsUnchanged()
    {
        string input = new string('a', 120);

        string result = ChatTerminalRenderer.SummarizePrompt(input);

        Assert.Equal(input, result);
    }

    /// <summary>
    /// An input exceeding 120 characters should be truncated to 117 characters plus "...".
    /// </summary>
    [Fact]
    public void SummarizePrompt_OverLimit_TruncatesWithEllipsis()
    {
        string input = new string('b', 200);

        string result = ChatTerminalRenderer.SummarizePrompt(input);

        Assert.Equal(120, result.Length);
        Assert.EndsWith("...", result);
        Assert.Equal(new string('b', 117) + "...", result);
    }

    /// <summary>
    /// Multiline input should be collapsed to a single line before truncation.
    /// </summary>
    [Fact]
    public void SummarizePrompt_MultilineInput_CollapsesToSingleLine()
    {
        string input = "Line one" + System.Environment.NewLine + "Line two";

        string result = ChatTerminalRenderer.SummarizePrompt(input);

        Assert.DoesNotContain(System.Environment.NewLine, result);
        Assert.Equal("Line one Line two", result);
    }
}
