namespace ArchHarness.App.Core;

/// <summary>
/// Classifies exceptions as structured-output parse failures for consistent handling across agents.
/// </summary>
internal static class StructuredOutputParser
{
    /// <summary>
    /// Determines whether the exception indicates a failure to parse structured output (JSON, markdown, schema).
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <returns><c>true</c> if the error is a structured-output parse failure; otherwise <c>false</c>.</returns>
    public static bool IsParseFailure(Exception ex)
    {
        string text = ex.ToString();
        return text.Contains("parse", StringComparison.OrdinalIgnoreCase)
            || text.Contains("json", StringComparison.OrdinalIgnoreCase)
            || text.Contains("markdown", StringComparison.OrdinalIgnoreCase)
            || text.Contains("schema", StringComparison.OrdinalIgnoreCase);
    }
}
