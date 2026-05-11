using ArchHarness.App.Agents;

namespace ArchHarness.App.Tests.Agents;

public sealed class PromptLoaderTests
{
    [Fact]
    public void Load_WhenPromptFileMissing_ThrowsClearConfigurationError()
    {
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            PromptLoader.Load("MissingPromptGroup", "missing-prompt.md"));

        Assert.Contains("Required prompt file could not be loaded", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Prompts/MissingPromptGroup/missing-prompt.md", ex.Message, StringComparison.Ordinal);
    }
}
