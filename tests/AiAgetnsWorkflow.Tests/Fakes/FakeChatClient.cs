using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AiAgetnsWorkflow.Tests.Fakes;

public sealed class FakeChatClient : IChatClient
{
    public IEnumerable<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }

    /// <summary>Every request received via GetResponseAsync, in order.</summary>
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = new();

    public int CallCount => Requests.Count;

    /// <summary>Canned assistant reply text.</summary>
    public string ResponseText { get; set; } = "ok";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        LastMessages = snapshot;
        LastOptions = options;
        Requests.Add(snapshot);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ResponseText)));
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
