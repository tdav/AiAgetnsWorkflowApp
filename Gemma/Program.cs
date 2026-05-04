using LMKit.Model;
using LMKit.Integrations.ExtensionsAI.ChatClient;
using Microsoft.Extensions.AI;

// Load a model
using var model = LM.LoadFromModelID("gemma4:e2b");

// Create an IChatClient
IChatClient client = new LMKitChatClient(model);

// Use it
await foreach (var update in client.GetStreamingResponseAsync("создать c# код которвый аудио переобразование фуре"))
{
    Console.Write(update.Text);
}

Console.WriteLine();
Console.ReadKey();