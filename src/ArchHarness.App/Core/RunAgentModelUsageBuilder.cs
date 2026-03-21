namespace ArchHarness.App.Core;

/// <summary>
/// Builds the agent model usage section written into persisted run logs.
/// </summary>
public interface IRunAgentModelUsageBuilder
{
    /// <summary>
    /// Builds the agent model usage entries for the supplied overrides.
    /// </summary>
    object[] Build(IDictionary<string, string>? overrides);
}

/// <summary>
/// Default implementation of <see cref="IRunAgentModelUsageBuilder"/>.
/// </summary>
public sealed class RunAgentModelUsageBuilder : IRunAgentModelUsageBuilder
{
    private static readonly string[] _roles = new[]
    {
        "orchestration",
        "frontend-developer",
        "backend-developer",
        "build",
        "coding-style",
        "security",
        "architecture"
    };

    private readonly IModelResolver _modelResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunAgentModelUsageBuilder"/> class.
    /// </summary>
    public RunAgentModelUsageBuilder(IModelResolver modelResolver)
    {
        this._modelResolver = modelResolver;
    }

    /// <inheritdoc />
    public object[] Build(IDictionary<string, string>? overrides)
        => _roles
            .Select(role => new { role, model = this._modelResolver.Resolve(role, overrides) })
            .Cast<object>()
            .ToArray();
}
