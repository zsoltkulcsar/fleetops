using Xunit;

namespace FleetOps.Tests;

public class PipelineProofTests
{
    [Fact]
    public void Deliberate_failure_proves_main_refuses_a_bad_commit() => Assert.Fail("Deliberate pipeline proof");
}
