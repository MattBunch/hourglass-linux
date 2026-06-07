using Hourglass.Core;
using Xunit;

namespace Hourglass.Core.Tests;

public sealed class PortScaffoldTests
{
    [Fact]
    public void Scaffold_has_expected_name()
    {
        var scaffold = new PortScaffold();

        Assert.Equal("Hourglass Linux Port Core", scaffold.Name);
    }
}
