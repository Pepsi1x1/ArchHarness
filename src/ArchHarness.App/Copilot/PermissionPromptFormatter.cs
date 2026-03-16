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
                if (!string.IsNullOrWhiteSpace(shell.Intention))
                {
                    lines.Add($"Intent: {shell.Intention}");
                }

                if (!string.IsNullOrWhiteSpace(shell.FullCommandText))
                {
                    lines.Add($"Command: {shell.FullCommandText}");
                }

                break;
            case PermissionRequestWrite write:
                if (!string.IsNullOrWhiteSpace(write.Intention))
                {
                    lines.Add($"Intent: {write.Intention}");
                }

                if (!string.IsNullOrWhiteSpace(write.FileName))
                {
                    lines.Add($"File: {write.FileName}");
                }

                break;
            case PermissionRequestRead read:
                if (!string.IsNullOrWhiteSpace(read.Intention))
                {
                    lines.Add($"Intent: {read.Intention}");
                }

                if (!string.IsNullOrWhiteSpace(read.Path))
                {
                    lines.Add($"Path: {read.Path}");
                }

                break;
            case PermissionRequestUrl url:
                if (!string.IsNullOrWhiteSpace(url.Intention))
                {
                    lines.Add($"Intent: {url.Intention}");
                }

                if (!string.IsNullOrWhiteSpace(url.Url))
                {
                    lines.Add($"URL: {url.Url}");
                }

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
                if (!string.IsNullOrWhiteSpace(memory.Subject))
                {
                    lines.Add($"Subject: {memory.Subject}");
                }

                break;
        }

        lines.Add("Approve this request?");
        return string.Join(Environment.NewLine, lines);
    }
}