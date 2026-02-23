namespace ArchHarness.App.Copilot;

public interface ICopilotSessionFactory
{
    ICopilotSession Create(string model);
}

public sealed class CopilotSessionFactory : ICopilotSessionFactory
{
    public ICopilotSession Create(string model) => new InMemoryCopilotSession(model);

    private sealed class InMemoryCopilotSession(string model) : ICopilotSession
    {
        public Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken)
        {
            // Local deterministic placeholder for Copilot SDK-backed calls.
            return Task.FromResult($"[{model}] {prompt}");
        }
    }
}
