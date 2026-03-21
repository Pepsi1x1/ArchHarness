namespace ArchHarness.App.Tui;

/// <summary>
/// Wraps console input access for terminal UI collaborators.
/// </summary>
public interface IConsoleInputReader
{
    /// <summary>
    /// Gets a value indicating whether input is redirected.
    /// </summary>
    bool IsInputRedirected { get; }

    /// <summary>
    /// Gets a value indicating whether a key is available.
    /// </summary>
    bool KeyAvailable { get; }

    /// <summary>
    /// Tries to read a key without echoing it.
    /// </summary>
    bool TryReadKey(out ConsoleKeyInfo keyInfo);
}

/// <summary>
/// Default implementation of <see cref="IConsoleInputReader"/>.
/// </summary>
public sealed class ConsoleInputReader : IConsoleInputReader
{
    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;

    /// <inheritdoc />
    public bool KeyAvailable => !Console.IsInputRedirected && Console.KeyAvailable;

    /// <inheritdoc />
    public bool TryReadKey(out ConsoleKeyInfo keyInfo)
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
