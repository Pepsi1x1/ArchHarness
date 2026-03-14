namespace ArchHarness.App.Storage;

/// <summary>
/// Represents a previewable run artifact file.
/// </summary>
/// <param name="Name">The artifact file name.</param>
/// <param name="FullPath">The full file-system path to the artifact.</param>
/// <param name="Kind">The classified artifact kind.</param>
/// <param name="Description">The display description for the artifact.</param>
/// <param name="Preview">A truncated text preview of the artifact contents.</param>
public sealed record RunArtifactPreview(string Name, string FullPath, string Kind, string Description, string Preview);