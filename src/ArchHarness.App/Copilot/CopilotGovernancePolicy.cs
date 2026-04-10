using System.Text.RegularExpressions;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Defines the contract for governance policies applied to Copilot tool usage.
/// </summary>
public interface ICopilotGovernancePolicy
{
    /// <summary>
    /// Evaluates a tool invocation before execution and returns an allow/deny decision.
    /// </summary>
    /// <param name="input">The pre-tool-use hook input.</param>
    /// <returns>The governance decision output.</returns>
    Task<PreToolUseHookOutput> OnPreToolUseAsync(PreToolUseHookInput input);

    /// <summary>
    /// Processes a tool invocation after execution for auditing purposes.
    /// </summary>
    /// <param name="input">The post-tool-use hook input.</param>
    /// <returns>The post-tool-use output.</returns>
    Task<PostToolUseHookOutput> OnPostToolUseAsync(PostToolUseHookInput input);
}

/// <summary>
/// Default governance policy that denies potentially destructive tool operations.
/// </summary>
public sealed class CopilotGovernancePolicy : ICopilotGovernancePolicy
{
    private readonly IToolUsageLogger _toolUsageLogger;

    /// <summary>
    /// Initializes a new instance of <see cref="CopilotGovernancePolicy"/>.
    /// </summary>
    /// <param name="toolUsageLogger">The tool usage logger for audit trails.</param>
    public CopilotGovernancePolicy(IToolUsageLogger toolUsageLogger)
    {
        this._toolUsageLogger = toolUsageLogger;
    }

    private static readonly string[] DENIED_TOOL_NAME_FRAGMENTS =
    {
        "delete",
        "remove",
        "truncate",
        "drop",
        "format"
    };

    /// <inheritdoc />
    public async Task<PreToolUseHookOutput> OnPreToolUseAsync(PreToolUseHookInput input)
    {
        string toolName = input.ToolName ?? string.Empty;
        bool denyByName = DENIED_TOOL_NAME_FRAGMENTS.Any(fragment => toolName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        bool denyByArgs = LooksDestructive(input.ToolArgs);
        string decision = denyByName || denyByArgs ? "deny" : "allow";

        await this._toolUsageLogger.LogPreToolUseAsync(input, decision, denyByName, denyByArgs);

        if (decision == "deny")
        {
            return new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                AdditionalContext = "Tool denied by governance policy: potentially destructive operation."
            };
        }

        return new PreToolUseHookOutput
        {
            PermissionDecision = "allow",
            ModifiedArgs = input.ToolArgs,
            AdditionalContext = "Tool allowed by governance policy."
        };
    }

    /// <inheritdoc />
    public async Task<PostToolUseHookOutput> OnPostToolUseAsync(PostToolUseHookInput input)
    {
        if (IsFailureHookInput(input))
        {
            return new PostToolUseHookOutput
            {
                AdditionalContext = "Tool failure observed under governance audit."
            };
        }

        await this._toolUsageLogger.LogPostToolUseAsync(input);
        return new PostToolUseHookOutput
        {
            AdditionalContext = $"Tool '{input.ToolName}' completed under governance audit."
        };
    }

    private static bool IsFailureHookInput(PostToolUseHookInput input)
        => input.GetType().GetProperty("Error") is not null;

    private static readonly Regex DESTRUCTIVE_PATTERN_REGEX = new Regex(
        "(?i)(rm\\s+-rf|drop\\s+table|truncate\\s+table|del\\s+/f|format\\s+[a-z]:)",
        RegexOptions.Compiled | RegexOptions.NonBacktracking);

    private static bool LooksDestructive(object? toolArgs)
    {
        if (toolArgs is null)
        {
            return false;
        }

        string serialized = System.Text.Json.JsonSerializer.Serialize(toolArgs);
        return DESTRUCTIVE_PATTERN_REGEX.IsMatch(serialized);
    }
}
