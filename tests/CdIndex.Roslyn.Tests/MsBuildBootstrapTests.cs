using Xunit;
using CdIndex.Roslyn;

public class MsBuildBootstrapTests
{
    [Fact]
    public void EnsureRegistered_IsIdempotent()
    {
        MsBuildBootstrap.EnsureRegistered();
        MsBuildBootstrap.EnsureRegistered();
        // No exception should be thrown
    }
}
