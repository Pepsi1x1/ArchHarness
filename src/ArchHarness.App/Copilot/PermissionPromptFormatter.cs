using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Formats Copilot permission requests into host-displayable text.
/// </summary>
public static class PermissionPromptFormatter
{
    /// <summary>
    /// Builds a human-readable permission approval prompt.
    /// </summary>
    /// <param name="request">The permission request details.</param>
    /// <param name="invocation">The invocation context.</param>
    /// <returns>A multi-line approval prompt.</returns>
    public static string BuildQuestion(PermissionRequest request, PermissionInvocation invocation)
    {
        List<string> lines = new List<string>
        {
            $"Copilot requested permission for {request.Kind}.",
            $"Session: {invocation.SessionId}"
        };

        switch (request)
        {
            case PermissionRequestShell shell:
                AddIntent(lines, shell.Intention);
                AddDetail(lines, "Command", shell.FullCommandText);
                break;
            case PermissionRequestWrite write:
                AddIntent(lines, write.Intention);
                AddDetail(lines, "File", write.FileName);
                break;
            case PermissionRequestRead read:
                AddIntent(lines, read.Intention);
                AddDetail(lines, "Path", read.Path);
                break;
            case PermissionRequestUrl url:
                AddIntent(lines, url.Intention);
                AddDetail(lines, "URL", url.Url);
                break;
            case PermissionRequestMcp mcp:
                lines.Add($"Tool: {mcp.ServerName}/{mcp.ToolName}");
                break;
            case PermissionRequestCustomTool customTool:
                lines.Add($"Tool: {customTool.ToolName}");
                break;
            case PermissionRequestHook hook:
                lines.Add($"Hook: {hook.ToolName}");
                break;
            case PermissionRequestMemory memory:
                AddDetail(lines, "Subject", memory.Subject);
                break;
        }

        lines.Add("Approve this request?");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddIntent(List<string> lines, string? intention)
        => AddDetail(lines, "Intent", intention);

    private static void AddDetail(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }
}
