using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Fakes;

public sealed class FakeChatClient : IChatClient
{
    public IEnumerable<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        LastOptions = options;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages;
        LastOptions = options;
        return EmptyAsync();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
