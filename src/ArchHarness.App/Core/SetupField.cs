namespace ArchHarness.App.Core;

/// <summary>
/// Represents a single field in the interactive setup form.
/// </summary>
/// <param name="Id">The unique identifier for the field.</param>
/// <param name="Label">The display label for the field.</param>
/// <param name="Value">The current value of the field.</param>
internal sealed record SetupField(string Id, string Label, string Value);
