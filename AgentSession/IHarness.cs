namespace AgentSession;

internal interface IHarness
{
    Task RunAsync(CancellationToken cancellationToken = default);
}
