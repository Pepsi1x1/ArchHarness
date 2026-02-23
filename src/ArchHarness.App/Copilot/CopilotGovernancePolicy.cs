using System.Text.RegularExpressions;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

public interface ICopilotGovernancePolicy
{
    Task<PreToolUseHookOutput> OnPreToolUseAsync(PreToolUseHookInput input);
    Task<PostToolUseHookOutput> OnPostToolUseAsync(PostToolUseHookInput input);
}

public sealed class CopilotGovernancePolicy : ICopilotGovernancePolicy
{
    private static readonly string[] DeniedToolNameFragments =
    {
        "delete",
        "remove",
        "truncate",
        "drop",
        "format"
    };

    public Task<PreToolUseHookOutput> OnPreToolUseAsync(PreToolUseHookInput input)
    {
        var toolName = input.ToolName ?? string.Empty;
        var denyByName = DeniedToolNameFragments.Any(fragment => toolName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        var denyByArgs = LooksDestructive(input.ToolArgs);

        if (denyByName || denyByArgs)
        {
            return Task.FromResult(new PreToolUseHookOutput
            {
                PermissionDecision = "deny",
                AdditionalContext = "Tool denied by governance policy: potentially destructive operation."
            });
        }

        return Task.FromResult(new PreToolUseHookOutput
        {
            PermissionDecision = "allow",
            ModifiedArgs = input.ToolArgs,
            AdditionalContext = "Tool allowed by governance policy."
        });
    }

    public Task<PostToolUseHookOutput> OnPostToolUseAsync(PostToolUseHookInput input)
    {
        return Task.FromResult(new PostToolUseHookOutput
        {
            AdditionalContext = $"Tool '{input.ToolName}' completed under governance audit."
        });
    }

    private static bool LooksDestructive(object? toolArgs)
    {
        if (toolArgs is null)
        {
            return false;
        }

        var serialized = System.Text.Json.JsonSerializer.Serialize(toolArgs);
        return Regex.IsMatch(serialized, "(?i)(rm\\s+-rf|drop\\s+table|truncate\\s+table|del\\s+/f|format\\s+[a-z]:)");
    }
}
