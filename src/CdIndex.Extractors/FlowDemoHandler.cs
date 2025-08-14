namespace CdIndex.Extractors;

// Minimal demo handler so that --scan-flow can be run against this repository itself.
// Demonstrates patterns: guard + collapsed delegate (if+return), simple guard, and plain delegate.
internal sealed class FlowDemoHandler
{
    private readonly DemoFacade _facade = new();

    public void HandleAsync()
    {
        if (EnvEnabled())
        {
            _facade.Process();
            return; // collapsed pattern -> recorded as delegate only
        }

        if (ShouldRoute()) _facade.Route(); // guard + delegate

        _facade.Finish(); // delegate
    }

    private bool EnvEnabled() => true;
    private bool ShouldRoute() => false;
}

// Matches delegate heuristic (ends with Facade)
internal sealed class DemoFacade
{
    public void Process() { }
    public void Route() { }
    public void Finish() { }
}
