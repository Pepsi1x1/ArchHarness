using ArchHarness.Desktop.ViewModels;

namespace ArchHarness.App.Tests.Desktop;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void CreateDesignInstance_PopulatesDesktopPreviewState()
    {
        MainWindowViewModel viewModel = MainWindowViewModel.CreateDesignInstance();

        Assert.Equal("Design preview", viewModel.RunStatus);
        Assert.NotEmpty(viewModel.RecentRuns);
        Assert.NotEmpty(viewModel.Artifacts);
        Assert.NotEmpty(viewModel.AvailableAgents);
        Assert.NotEmpty(viewModel.SessionEvents);
        Assert.Contains("Desktop design preview", viewModel.SessionEvents[0].Detail);
    }

    [Fact]
    public void SessionEventItemViewModel_BuildsReadableLabel()
    {
        SessionEventItemViewModel viewModel = new SessionEventItemViewModel(
            "Session started",
            "12:15:00",
            "gpt-5.4",
            "session-123",
            "Connected successfully.");

        Assert.Equal("gpt-5.4 • session-123", viewModel.SessionLabel);
    }
}