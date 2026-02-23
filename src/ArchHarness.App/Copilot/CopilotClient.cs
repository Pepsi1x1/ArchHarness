namespace ArchHarness.App.Copilot;

public sealed class CopilotClient
{
    private readonly CopilotSessionFactory _sessionFactory;

    public CopilotClient(CopilotSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public Task<string> CompleteAsync(string model, string prompt, CancellationToken cancellationToken = default)
    {
        var session = _sessionFactory.Create(model);
        return session.CompleteAsync(prompt, cancellationToken);
    }
}

public interface ICopilotSession
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken);
}
