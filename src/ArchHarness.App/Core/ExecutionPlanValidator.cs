using System.Text.Json;

namespace ArchHarness.App.Core;

/// <summary>
/// Validates the schema of a raw execution plan JSON element before construction begins.
/// Ensures all required fields are present and step definitions are structurally valid.
/// </summary>
internal static class ExecutionPlanValidator
{
    private static readonly HashSet<string> AllowedAgents = new HashSet<string>(StringComparer.Ordinal)
    {
        "frontenddeveloper",
        "frontend-developer",
        "backenddeveloper",
        "backend-developer",
        "build",
        "codingstyle",
        "coding-style",
        "security",
        "secure",
        "architecture",
        "review"
    };

    /// <summary>
    /// Validates the top-level schema of an execution plan JSON element.
    /// </summary>
    /// <param name="root">The root JSON element to validate.</param>
    /// <param name="error">When validation fails, a description of the error.</param>
    /// <returns><c>true</c> if the schema is valid; otherwise <c>false</c>.</returns>
    public static bool ValidatePlanSchema(JsonElement root, out string? error)
    {
        error = null;

        if (!TryGetRequiredArray(root, "steps", out JsonElement stepsEl, out error))
        {
            return false;
        }

        List<JsonElement> stepsArray = stepsEl.EnumerateArray().ToList();
        if (!ValidateStepCount(stepsArray.Count, out error))
        {
            return false;
        }

        for (int i = 0; i < stepsArray.Count; i++)
        {
            if (!ValidateStep(stepsArray[i], i, out error))
            {
                return false;
            }
        }

        if (!TryGetRequiredObject(root, "iterationStrategy", out _, out error))
        {
            return false;
        }

        if (!TryGetRequiredArray(root, "completionCriteria", out JsonElement criteriaEl, out error))
        {
            return false;
        }

        List<JsonElement> criteria = criteriaEl.EnumerateArray().ToList();
        return ValidateCompletionCriteria(criteria.Count, out error);
    }

    private static bool ValidateStepCount(int stepCount, out string? error)
    {
        if (stepCount == 0)
        {
            error = "Field 'steps' array is empty. Must include at least one step.";
            return false;
        }

        if (stepCount > 10)
        {
            error = $"Too many steps ({stepCount}). Maximum 10 steps supported.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool ValidateStep(JsonElement step, int index, out string? error)
    {
        if (step.ValueKind != JsonValueKind.Object)
        {
            error = $"Step {index}: must be an object.";
            return false;
        }

        if (!TryGetRequiredString(step, "agent", out string? agentValue, out error))
        {
            error = $"Step {index}: missing or empty 'agent' field.";
            return false;
        }

        string normalizedAgent = agentValue!.Trim().ToLowerInvariant();
        if (!AllowedAgents.Contains(normalizedAgent))
        {
            error = $"Step {index}: agent '{normalizedAgent}' is not recognized. Use one of: FrontendDeveloper, BackendDeveloper, Build, CodingStyle, Security, Architecture.";
            return false;
        }

        if (!TryGetRequiredString(step, "objective", out _, out error))
        {
            error = $"Step {index}: missing or empty 'objective' field.";
            return false;
        }

        return ValidateDependencies(step, index, out error);
    }

    private static bool ValidateDependencies(JsonElement step, int index, out string? error)
    {
        if (!step.TryGetProperty("dependsOn", out JsonElement depsEl) || depsEl.ValueKind != JsonValueKind.Array)
        {
            error = null;
            return true;
        }

        foreach (JsonElement dep in depsEl.EnumerateArray())
        {
            if (!dep.TryGetInt32(out int depId) || depId <= 0)
            {
                error = $"Step {index}: dependsOn contains invalid ID. All dependency IDs must be positive integers (references to prior step IDs).";
                return false;
            }
        }

        error = null;
        return true;
    }

    private static bool ValidateCompletionCriteria(int criteriaCount, out string? error)
    {
        if (criteriaCount == 0)
        {
            error = "Field 'completionCriteria' is empty. Must include at least one completion criterion.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetRequiredArray(JsonElement root, string propertyName, out JsonElement value, out string? error)
    {
        if (!root.TryGetProperty(propertyName, out value))
        {
            error = $"Missing required field: '{propertyName}'";
            return false;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"Field '{propertyName}' must be an array.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetRequiredObject(JsonElement root, string propertyName, out JsonElement value, out string? error)
    {
        if (!root.TryGetProperty(propertyName, out value))
        {
            error = $"Missing required field: '{propertyName}'";
            return false;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            error = $"Field '{propertyName}' must be an object.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string? value, out string? error)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property))
        {
            value = null;
            error = $"Missing required field: '{propertyName}'";
            return false;
        }

        value = property.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Field '{propertyName}' must be a non-empty string.";
            return false;
        }

        error = null;
        return true;
    }
}
