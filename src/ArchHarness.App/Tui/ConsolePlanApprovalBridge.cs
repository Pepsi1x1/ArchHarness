using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Console/TUI implementation of <see cref="IPlanApprovalBridge"/> that renders the spec and
/// plan summary in the terminal and blocks until the user approves, regenerates, or cancels.
/// </summary>
public sealed class ConsolePlanApprovalBridge : IPlanApprovalBridge
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<PlanApprovalResponse> RequestApprovalAsync(
        PlanApprovalRequest request,
        CancellationToken cancellationToken)
    {
        await this._gate.WaitAsync(cancellationToken);
        try
        {
            return RenderAndReadApproval(request);
        }
        finally
        {
            this._gate.Release();
        }
    }

    private static PlanApprovalResponse RenderAndReadApproval(PlanApprovalRequest request)
    {
        int width = Math.Max(60, Console.WindowWidth - 1);

        Console.Clear();
        Console.SetCursorPosition(0, 0);

        WriteLineAt(0, string.Empty, width, ConsoleColor.Gray);

        Console.SetCursorPosition(0, 1);
        Console.BackgroundColor = ConsoleColor.DarkCyan;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("  ◈ PLAN APPROVAL ".PadRight(width));
        Console.ResetColor();

        int row = 3;

        // Spec summary
        WriteLineAt(row++, "── Spec ──", width, ConsoleColor.Cyan);
        row = RenderWrapped(request.SpecMarkdown, row, width, ConsoleColor.Gray);
        row++;

        // Plan summary
        WriteLineAt(row++, "── Plan ──", width, ConsoleColor.Cyan);
        row = RenderWrapped(request.PlanSummary, row, width, ConsoleColor.Gray);
        row++;

        // Choices
        WriteLineAt(row++, string.Empty, width, ConsoleColor.Gray);
        WriteLineAt(row++, "  [A] Approve   [R] Regenerate   [C] Cancel", width, ConsoleColor.Yellow);
        WriteLineAt(row, string.Empty, width, ConsoleColor.Gray);

        Console.Write("  Your choice> ");
        Console.ForegroundColor = ConsoleColor.Cyan;

        bool restoreCursor = TryGetCursorVisible();
        TrySetCursorVisible(true);
        try
        {
            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                switch (char.ToUpperInvariant(key.KeyChar))
                {
                    case 'A':
                        Console.WriteLine("Approved");
                        Console.ResetColor();
                        return new PlanApprovalResponse(PlanApprovalDecisions.APPROVED);

                    case 'R':
                        Console.ResetColor();
                        Console.Write("\n  Reason (optional)> ");
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        string? reason = TryReadLine();
                        Console.ResetColor();
                        return new PlanApprovalResponse(
                            PlanApprovalDecisions.REGENERATE,
                            string.IsNullOrWhiteSpace(reason) ? null : reason);

                    case 'C':
                        Console.WriteLine("Canceled");
                        Console.ResetColor();
                        return new PlanApprovalResponse(PlanApprovalDecisions.CANCELED);
                }
            }
        }
        finally
        {
            TrySetCursorVisible(restoreCursor);
        }
    }

    private static int RenderWrapped(string text, int startRow, int width, ConsoleColor color)
    {
        int row = startRow;
        int contentWidth = Math.Max(20, width - 4);

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length <= contentWidth)
            {
                WriteLineAt(row++, $"  {line}", width, color);
            }
            else
            {
                for (int offset = 0; offset < line.Length; offset += contentWidth)
                {
                    int len = Math.Min(contentWidth, line.Length - offset);
                    WriteLineAt(row++, $"  {line.Substring(offset, len)}", width, color);
                }
            }
        }

        return row;
    }

    private static void WriteLineAt(int row, string text, int width, ConsoleColor color)
    {
        Console.SetCursorPosition(0, row);
        Console.ForegroundColor = color;
        string output = text.Length > width ? text[..width] : text;
        Console.Write(output.PadRight(width));
        Console.ResetColor();
    }

    private static bool TryGetCursorVisible()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }
        try { return Console.CursorVisible; }
        catch { return false; }
    }

    private static void TrySetCursorVisible(bool visible)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try { Console.CursorVisible = visible; }
        catch { /* Ignore terminal capability failures. */ }
    }

    private static string? TryReadLine()
    {
        try { return Console.ReadLine(); }
        catch (IOException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
}
