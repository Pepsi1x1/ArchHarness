namespace ArchHarness.Web.Services;

/// <summary>
/// Payload for completing a pending user-input interaction.
/// </summary>
/// <param name="Answer">The submitted answer text.</param>
public sealed record UserInputSubmission(string? Answer);

/// <summary>
/// Payload for completing a pending permission interaction.
/// </summary>
/// <param name="Approved">Whether the request is approved.</param>
public sealed record PermissionSubmission(bool Approved);