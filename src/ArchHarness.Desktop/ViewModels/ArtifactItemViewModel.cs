namespace ArchHarness.Desktop.ViewModels;

public sealed class ArtifactItemViewModel : ViewModelBase
{
    public ArtifactItemViewModel(string name, string fullPath, string kind, string description, string preview)
    {
        this.Name = name;
        this.FullPath = fullPath;
        this.Kind = kind;
        this.Description = description;
        this.Preview = preview;
    }

    public string Name { get; }

    public string FullPath { get; }

    public string Kind { get; }

    public string Description { get; }

    public string Preview { get; }
}