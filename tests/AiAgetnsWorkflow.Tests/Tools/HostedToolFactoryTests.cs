using MagenticWorkflowApp.Services;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Tools;

public class HostedToolFactoryTests
{
    [Fact]
    public void Create_EmptyList_ReturnsEmpty()
    {
        new HostedToolFactory().Create(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void Create_KnownName_ReturnsCodeInterpreterTool()
    {
        var tools = new HostedToolFactory().Create(new[] { "CodeInterpreter" });
        tools.Should().ContainSingle();
        tools[0].Should().BeAssignableTo<AITool>();
    }

    [Fact]
    public void Create_UnknownName_Throws()
    {
        var act = () => new HostedToolFactory().Create(new[] { "Bogus" });
        act.Should().Throw<NotSupportedException>().WithMessage("*Bogus*");
    }
}
