using Avalonia.Controls;
using Avalonia.Interactivity;
using ArchHarness.Desktop.ViewModels;

namespace ArchHarness.Desktop;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
        : this(ResolveViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        this._viewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = viewModel;
        this.Opened += this.OnOpened;
        this.FindControl<Button>("RefreshWorkspaceButton")!.Click += this.OnRefreshWorkspaceClicked;
        this.FindControl<Button>("GenerateSummaryButton")!.Click += this.OnGenerateSummaryClicked;
        this.FindControl<Button>("StartRunButton")!.Click += this.OnStartRunClicked;
        this.FindControl<Button>("CancelRunButton")!.Click += this.OnCancelRunClicked;
        this.FindControl<ListBox>("RunsListBox")!.SelectionChanged += this.OnRunSelectionChanged;
        this.FindControl<ListBox>("ArtifactsListBox")!.SelectionChanged += this.OnArtifactSelectionChanged;
    }

    private static MainWindowViewModel ResolveViewModel()
    {
        if (DesktopHostContext.TryGetRequiredService<MainWindowViewModel>(out MainWindowViewModel? viewModel) && viewModel is not null)
        {
            return viewModel;
        }

        return MainWindowViewModel.CreateDesignInstance();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        await this._viewModel.InitializeAsync();
    }

    private async void OnRefreshWorkspaceClicked(object? sender, RoutedEventArgs e)
    {
        await this._viewModel.RefreshWorkspaceAsync();
    }

    private async void OnGenerateSummaryClicked(object? sender, RoutedEventArgs e)
    {
        await this._viewModel.GenerateSetupSummaryAsync();
    }

    private async void OnStartRunClicked(object? sender, RoutedEventArgs e)
    {
        await this._viewModel.StartRunAsync();
    }

    private async void OnCancelRunClicked(object? sender, RoutedEventArgs e)
    {
        await this._viewModel.CancelRunAsync();
    }

    private async void OnRunSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.FindControl<ListBox>("RunsListBox")!.SelectedItem is RunSummaryViewModel run)
        {
            await this._viewModel.SelectRunAsync(run);
        }
    }

    private void OnArtifactSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (this.FindControl<ListBox>("ArtifactsListBox")!.SelectedItem is ArtifactItemViewModel artifact)
        {
            this._viewModel.SelectArtifact(artifact);
        }
    }
}