using FleetOps.Kernel.Files;
using Xunit;

namespace FleetOps.Tests;

public sealed class ObjectLinkTests
{
    [Fact]
    public void Object_link_text_never_contains_the_url()
    {
        const string secretUrl = "https://objects.test/documents/private?signature=secret";
        var link = new ObjectLink(secretUrl, DateTimeOffset.UtcNow.AddMinutes(5));
        Assert.DoesNotContain(secretUrl, link.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secretUrl, $"{link}", StringComparison.Ordinal);
    }
}
