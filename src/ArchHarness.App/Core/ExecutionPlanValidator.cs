using System.Text.Json;

namespace ArchHarness.App.Core;

/// <summary>
/// Validates the schema of a raw execution plan JSON element before construction begins.
/// Ensures all required fields are present and step definitions are structurally valid.
/// </summary>
internal sealed class ExecutionPlanValidator
{
    /// <summary>
    /// Validates the top-level schema of an execution plan JSON element.
    /// </summary>
    /// <param name="root">The root JSON element to validate.</param>
    /// <param name="error">When validation fails, a description of the error.</param>
    /// <returns><c>true</c> if the schema is valid; otherwise <c>false</c>.</returns>
    public static bool ValidatePlanSchema(JsonElement root, out string? error)
    {
        error = null;

        if (!root.TryGetProperty("steps", out JsonElement stepsEl))
        {
            error = "Missing required field: 'steps'";
            return false;
        }

        if (stepsEl.ValueKind != JsonValueKind.Array)
        {
            error = "Field 'steps' must be an array.";
            return false;
        }

        List<JsonElement> stepsArray = stepsEl.EnumerateArray().ToList();
        if (stepsArray.Count == 0)
        {
            error = "Field 'steps' array is empty. Must include at least one step.";
            return false;
        }

        if (stepsArray.Count > 10)
        {
            error = $"Too many steps ({stepsArray.Count}). Maximum 10 steps supported.";
            return false;
        }

        for (int i = 0; i < stepsArray.Count; i++)
        {
            JsonElement step = stepsArray[i];
            if (step.ValueKind != JsonValueKind.Object)
            {
                error = $"Step {i}: must be an object.";
                return false;
            }

            if (!step.TryGetProperty("agent", out JsonElement agentEl) || string.IsNullOrWhiteSpace(agentEl.GetString()))
            {
                error = $"Step {i}: missing or empty 'agent' field.";
                return false;
            }

            string? agentValue = agentEl.GetString()?.Trim().ToLowerInvariant();
            if (!new[] { "frontenddeveloper", "frontend-developer", "backenddeveloper", "backend-developer", "build", "codingstyle", "coding-style", "security", "secure", "architecture", "review" }
                .Contains(agentValue))
            {
                error = $"Step {i}: agent '{agentValue}' is not recognized. Use one of: FrontendDeveloper, BackendDeveloper, Build, CodingStyle, Security, Architecture.";
                return false;
            }

            if (!step.TryGetProperty("objective", out JsonElement objEl) || string.IsNullOrWhiteSpace(objEl.GetString()))
            {
                error = $"Step {i}: missing or empty 'objective' field.";
                return false;
            }

            if (step.TryGetProperty("dependsOn", out JsonElement depsEl) && depsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement dep in depsEl.EnumerateArray())
                {
                    if (!dep.TryGetInt32(out int depId) || depId <= 0)
                    {
                        error = $"Step {i}: dependsOn contains invalid ID. All dependency IDs must be positive integers (references to prior step IDs).";
                        return false;
                    }
                }
            }
        }

        if (!root.TryGetProperty("iterationStrategy", out JsonElement iterEl))
        {
            error = "Missing required field: 'iterationStrategy'";
            return false;
        }

        if (iterEl.ValueKind != JsonValueKind.Object)
        {
            error = "Field 'iterationStrategy' must be an object.";
            return false;
        }

        if (!root.TryGetProperty("completionCriteria", out JsonElement criteriaEl))
        {
            error = "Missing required field: 'completionCriteria'";
            return false;
        }

        if (criteriaEl.ValueKind != JsonValueKind.Array)
        {
            error = "Field 'completionCriteria' must be an array.";
            return false;
        }

        List<JsonElement> criteria = criteriaEl.EnumerateArray().ToList();
        if (criteria.Count == 0)
        {
            error = "Field 'completionCriteria' is empty. Must include at least one completion criterion.";
            return false;
        }

        return true;
    }
}
