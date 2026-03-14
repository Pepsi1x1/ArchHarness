using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

/// <summary>
/// Console-backed setup status sink used by the terminal host.
/// </summary>
public sealed class ConsoleSetupStatusSink : ISetupStatusSink
{
	/// <inheritdoc />
	public void Clear() => Console.Clear();

	/// <inheritdoc />
	public void WriteLine(string message) => Console.WriteLine(message);
}