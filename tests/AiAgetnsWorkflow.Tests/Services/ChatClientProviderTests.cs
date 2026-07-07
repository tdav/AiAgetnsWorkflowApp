using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiAgetnsWorkflow.Tests.Services;

public class ChatClientProviderTests
{
    private static IConfiguration BuildConfig(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    private static ChatClientProvider CreateSut(IConfiguration configuration)
        => new(configuration, NullLoggerFactory.Instance, Substitute.For<IAgentActivityLogger>());

    [Test]
    public void HasCredentials_EmptyConfiguration_IsFalse()
    {
        CreateSut(BuildConfig()).HasCredentials.Should().BeFalse();
    }

    [Test]
    public void HasCredentials_OllamaEndpointConfigured_IsTrue()
    {
        var sut = CreateSut(BuildConfig(("Ollama:Endpoint", "http://localhost:11434")));
        sut.HasCredentials.Should().BeTrue();
    }

    [Test]
    public void GetChatClient_OllamaConfigured_ReturnsTrimmingClient()
    {
        var sut = CreateSut(BuildConfig(("Ollama:Endpoint", "http://localhost:11434")));
        using var client = sut.GetChatClient("llama3");
        client.Should().BeOfType<TokenTrimmingChatClient>();
    }

    [Test]
    public void GetChatClient_NoCredentials_ThrowsInvalidOperation()
    {
        var sut = CreateSut(BuildConfig());
        // Client construction is lazy for the window accessor but the raw client is built eagerly.
        var act = () => sut.GetChatClient("gpt-4");
        act.Should().Throw<InvalidOperationException>().WithMessage("*credentials*");
    }

    [Test]
    public void BuildKernel_AzureOnlyConfiguration_ThrowsNotSupported()
    {
        var sut = CreateSut(BuildConfig(("AzureOpenAI:Endpoint", "https://example.openai.azure.com")));
        var act = () => sut.BuildKernel("gpt-4");
        act.Should().Throw<NotSupportedException>().WithMessage("*Azure*");
    }

    [Test]
    public void BuildKernel_NoCredentials_ThrowsInvalidOperation()
    {
        var sut = CreateSut(BuildConfig());
        var act = () => sut.BuildKernel("gpt-4");
        act.Should().Throw<InvalidOperationException>().WithMessage("*credentials*");
    }

    [Test]
    public void BuildKernel_OllamaConfigured_ReturnsKernel()
    {
        var sut = CreateSut(BuildConfig(("Ollama:Endpoint", "http://localhost:11434")));
        sut.BuildKernel("llama3").Should().NotBeNull();
    }
}
