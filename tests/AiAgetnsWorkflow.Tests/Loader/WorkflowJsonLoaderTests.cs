using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Loader;

public class WorkflowJsonLoaderTests : IDisposable
{
    private const string FsRootVar = "TEST_FS_ROOT";

    public WorkflowJsonLoaderTests()
        => Environment.SetEnvironmentVariable(FsRootVar, "/tmp/data");

    public void Dispose()
        => Environment.SetEnvironmentVariable(FsRootVar, null);

    private static WorkflowJsonLoader CreateLoader()
        => new(NullLogger<WorkflowJsonLoader>.Instance);

    private static string Path(string fileName)
        => System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    [Test]
    public async Task LoadAsync_ValidMcpConfig_ParsesAndSubstitutesEnv()
    {
        var cfg = await CreateLoader().LoadConfigurationAsync(Path("workflow-with-mcp.json"));
        cfg.McpServers.Should().HaveCount(1);
        cfg.McpServers[0].Args[2].Should().Be("/tmp/data");
        cfg.Agents[0].McpServers.Should().BeEquivalentTo(new[] { "filesystem" });
    }

    [Test]
    public async Task LoadAsync_MissingMcpRef_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-missing-mcp-ref.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*nope*");
    }

    [Test]
    public async Task LoadAsync_StdioWithoutCommand_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-stdio-no-command.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*command*");
    }

    [Test]
    public async Task LoadAsync_HttpWithoutUrl_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-http-no-url.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*url*");
    }

    [Test]
    public async Task LoadAsync_DuplicateMcpName_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-duplicate-mcp.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*duplicate*");
    }

    [Test]
    public async Task LoadAsync_UnknownTransport_ThrowsValidation()
    {
        var act = () => CreateLoader().LoadConfigurationAsync(Path("workflow-invalid-bad-transport.json"));
        await act.Should().ThrowAsync<WorkflowValidationException>().WithMessage("*transport*smtp*");
    }
}
