namespace ArchHarness.App.Copilot;

public interface ICopilotClient
{
    Task<string> CompleteAsync(string model, string prompt, CancellationToken cancellationToken = default);
}

public sealed class CopilotClient : ICopilotClient
{
    private readonly ICopilotSessionFactory _sessionFactory;

    public CopilotClient(ICopilotSessionFactory sessionFactory)
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
