namespace ArchHarness.App.Core;

/// <summary>
/// Parses and validates execution plan JSON into strongly-typed <see cref="ExecutionPlan"/> instances.
/// </summary>
public interface IExecutionPlanParser
{
    /// <summary>
    /// Attempts to parse a raw model response into a validated <see cref="ExecutionPlan"/>.
    /// </summary>
    /// <param name="raw">The raw text response from the orchestration model.</param>
    /// <param name="workspaceRoot">The root path of the workspace used to enforce path constraints.</param>
    /// <param name="plan">When successful, the parsed execution plan.</param>
    /// <param name="validationError">When unsuccessful, a description of the validation failure.</param>
    /// <returns><c>true</c> if parsing and validation succeeded; otherwise <c>false</c>.</returns>
    bool TryBuildExecutionPlan(string raw, string workspaceRoot, out ExecutionPlan plan, out string? validationError);
}
