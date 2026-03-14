namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Represents a single run artifact for display in the desktop artifact list.
/// </summary>
public sealed class ArtifactItemViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactItemViewModel"/> class.
    /// </summary>
    /// <param name="name">The file name of the artifact.</param>
    /// <param name="fullPath">The full file-system path to the artifact.</param>
    /// <param name="kind">The classified artifact kind (e.g., JSON, Markdown).</param>
    /// <param name="description">A human-readable description including size and path metadata.</param>
    /// <param name="preview">A text preview of the artifact contents.</param>
    public ArtifactItemViewModel(string name, string fullPath, string kind, string description, string preview)
    {
        this.Name = name;
        this.FullPath = fullPath;
        this.Kind = kind;
        this.Description = description;
        this.Preview = preview;
    }

    /// <summary>Gets the file name of the artifact.</summary>
    public string Name { get; }

    /// <summary>Gets the full file-system path to the artifact.</summary>
    public string FullPath { get; }

    /// <summary>Gets the classified artifact kind (e.g., JSON, Markdown).</summary>
    public string Kind { get; }

    /// <summary>Gets a human-readable description including size and path metadata.</summary>
    public string Description { get; }

    /// <summary>Gets a text preview of the artifact contents.</summary>
    public string Preview { get; }
}