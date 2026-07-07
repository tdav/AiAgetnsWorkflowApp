using AiAgetnsWorkflow.Tests.Fakes;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;

namespace AiAgetnsWorkflow.Tests.Plugins;

public class AgentPluginRegistryTests
{
    [Test]
    public void TryGet_KnownName_ReturnsTrueAndPlugin()
    {
        IAgentPlugin a = new FakeAgentPlugin("A");
        var reg = new AgentPluginRegistry(new[] { a });
        reg.TryGet("A", out var found).Should().BeTrue();
        found.Should().BeSameAs(a);
    }

    [Test]
    public void TryGet_UnknownName_ReturnsFalse()
    {
        var reg = new AgentPluginRegistry(Array.Empty<IAgentPlugin>());
        reg.TryGet("missing", out var found).Should().BeFalse();
        found.Should().BeNull();
    }

    [Test]
    public void RegisteredNames_ReturnsAll()
    {
        var reg = new AgentPluginRegistry(new IAgentPlugin[] { new FakeAgentPlugin("A"), new FakeAgentPlugin("B") });
        reg.RegisteredNames.Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Test]
    public void Constructor_DuplicateName_Throws()
    {
        var act = () => new AgentPluginRegistry(new IAgentPlugin[] { new FakeAgentPlugin("A"), new FakeAgentPlugin("A") });
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate*A*");
    }
}
