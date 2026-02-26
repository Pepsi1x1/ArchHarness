using System.Text;

namespace ArchHarness.App.Core;

/// <summary>
/// Handles path input with tab-completion in the terminal.
/// </summary>
internal static class PathInputHandler
{
    /// <summary>
    /// Reads a file-system path from the console with tab-completion support.
    /// </summary>
    /// <param name="currentValue">The initial path value to display.</param>
    /// <returns>The user-entered path string.</returns>
    public static string ReadPathWithTabCompletion(string currentValue)
    {
        var buffer = new StringBuilder(currentValue ?? string.Empty);
        Console.Write(buffer.ToString());

        while (true)
        {
            if (!TryReadKey(out var key))
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (HandlePathSubmit(key.Key, buffer, out var submitted))
            {
                return submitted;
            }

            if (HandlePathBackspace(key.Key, buffer))
            {
                continue;
            }

            if (HandlePathCompletion(key.Key, buffer))
            {
                continue;
            }

            AppendPathCharacterIfPrintable(key.KeyChar, buffer);
        }
    }

    private static bool HandlePathSubmit(ConsoleKey key, StringBuilder buffer, out string value)
    {
        value = string.Empty;
        if (key != ConsoleKey.Enter)
        {
            return false;
        }

        Console.WriteLine();
        value = buffer.ToString();
        return true;
    }

    private static bool HandlePathBackspace(ConsoleKey key, StringBuilder buffer)
    {
        if (key != ConsoleKey.Backspace)
        {
            return false;
        }

        if (buffer.Length == 0)
        {
            return true;
        }

        buffer.Length--;
        Console.Write("\b \b");
        return true;
    }

    private static bool HandlePathCompletion(ConsoleKey key, StringBuilder buffer)
    {
        if (key != ConsoleKey.Tab)
        {
            return false;
        }

        var current = buffer.ToString();
        var completed = TryCompletePath(current);
        if (string.Equals(completed, current, StringComparison.Ordinal))
        {
            return true;
        }

        while (buffer.Length > 0)
        {
            buffer.Length--;
            Console.Write("\b \b");
        }

        buffer.Append(completed);
        Console.Write(completed);

        // Show a single-line tab-completion hint on the line below, then restore cursor
        try
        {
            var savedLeft = Console.CursorLeft;
            var savedTop = Console.CursorTop;
            var hintRow = savedTop + 1;
            if (hintRow < Console.BufferHeight)
            {
                Console.SetCursorPosition(0, hintRow);
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write($"  ↳ {completed}");
                Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 4 - completed.Length)));
                Console.ResetColor();
                Console.SetCursorPosition(savedLeft, savedTop);
            }
        }
        catch (IOException)
        {
            // Ignore hint rendering failures in non-interactive environments.
        }
        catch (InvalidOperationException)
        {
            // Ignore hint rendering failures in non-interactive environments.
        }

        return true;
    }

    private static void AppendPathCharacterIfPrintable(char keyChar, StringBuilder buffer)
    {
        if (char.IsControl(keyChar))
        {
            return;
        }

        buffer.Append(keyChar);
        Console.Write(keyChar);
    }

    private static string TryCompletePath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(input);
            var directoryPart = expanded;
            var prefix = string.Empty;

            if (!Directory.Exists(expanded))
            {
                directoryPart = Path.GetDirectoryName(expanded) ?? Directory.GetCurrentDirectory();
                prefix = Path.GetFileName(expanded) ?? string.Empty;
            }

            if (!Directory.Exists(directoryPart))
            {
                return input;
            }

            var matches = Directory.GetDirectories(directoryPart)
                .Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (matches.Length == 0)
            {
                return input;
            }

            var match = matches[0];
            return match.EndsWith(Path.DirectorySeparatorChar)
                ? match
                : match + Path.DirectorySeparatorChar;
        }
        catch
        {
            return input;
        }
    }

    private static bool TryReadKey(out ConsoleKeyInfo keyInfo)
    {
        keyInfo = default;
        try
        {
            keyInfo = Console.ReadKey(intercept: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
