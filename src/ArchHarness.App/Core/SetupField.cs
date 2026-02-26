namespace ArchHarness.App.Core;

/// <summary>
/// Represents a single field in the interactive setup form.
/// </summary>
/// <param name="Id">The unique identifier for the field.</param>
/// <param name="Label">The display label for the field.</param>
/// <param name="Value">The current value of the field.</param>
/// <param name="IsPlaceholderValue">When true, the value is placeholder hint text rather than a real value.</param>
internal sealed record SetupField(string Id, string Label, string Value, bool IsPlaceholderValue = false);
