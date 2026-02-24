using ArchHarness.App.Tui;

namespace ArchHarness.App.Tests.Tui;

/// <summary>
/// Verifies the spinner-to-phase dictionary in RunMonitor.
/// </summary>
public class RunMonitorTests
{
    /// <summary>
    /// Each spinner character should map to its expected phase description.
    /// </summary>
    [Theory]
    [InlineData('|', "Analyzing")]
    [InlineData('/', "Planning")]
    [InlineData('-', "Executing")]
    [InlineData('\\', "Finalizing")]
    public void SpinnerPhaseMap_ContainsExpectedMapping(char spinner, string expectedPhase)
    {
        Assert.True(RunMonitor.SPINNER_PHASE_MAP.ContainsKey(spinner));
        Assert.Equal(expectedPhase, RunMonitor.SPINNER_PHASE_MAP[spinner]);
    }

    /// <summary>
    /// The map should contain exactly four entries — one per spinner character.
    /// </summary>
    [Fact]
    public void SpinnerPhaseMap_ContainsExactlyFourEntries()
    {
        Assert.Equal(4, RunMonitor.SPINNER_PHASE_MAP.Count);
    }
}
