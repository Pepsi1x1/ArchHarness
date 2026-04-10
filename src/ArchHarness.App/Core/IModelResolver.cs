namespace ArchHarness.App.Core;

/// <summary>
/// Resolves the model identifier for a given agent role, applying overrides and validating against discovered Copilot models when available.
/// </summary>
public interface IModelResolver
{
    /// <summary>
    /// Resolves the model for the specified role, applying any overrides.
    /// </summary>
    /// <param name="role">The agent role identifier.</param>
    /// <param name="overrides">Optional model overrides keyed by role.</param>
    /// <returns>The resolved model identifier.</returns>
    string Resolve(string role, IDictionary<string, string>? overrides);

    /// <summary>
    /// Resolves the reasoning effort for the specified role.
    /// </summary>
    /// <param name="role">The agent role identifier.</param>
    /// <returns>The configured reasoning effort, or null when none is configured.</returns>
    string? ResolveReasoningEffort(string role);

    /// <summary>
    /// Validates that the specified model is in the discovered Copilot model list when that list is available.
    /// </summary>
    /// <param name="model">The model identifier to validate.</param>
    void ValidateOrThrow(string model);

    /// <summary>Gets the collection of discovered model identifiers, if available.</summary>
    IReadOnlyCollection<string> GetSupportedModels();

    /// <summary>
    /// Validates the configured default models and any request overrides against the discovered Copilot model catalog when available.
    /// </summary>
    /// <param name="overrides">Optional per-role overrides to validate alongside configured defaults.</param>
    void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null);
}
