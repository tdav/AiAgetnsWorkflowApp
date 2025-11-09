# AI Coding Assistant Instructions

## Общие правила
- Язык всех сообщений и комментариев: **русский**
- Все предложения должны быть **с высокой степенью достоверности**.  
  Избегать догадок, неполных решений и кода, который может привести к ошибкам.
- Перед выводом результата необходимо «мысленно проверить» его на корректность.


## Project Context

You are working on **MagenticWorkflowApp** - a .NET 8.0 console application for Microsoft Agent Framework workflow orchestration.

### Tech Stack
- **Platform:** .NET 8.0, C# 12
- **Framework:** Microsoft.Agents.AI.Workflows (preview)
- **Pattern:** DI-based console application
- **Configuration:** JSON-based

### Core Purpose
Universal platform supporting 4 workflow orchestration types with JSON configuration and automatic visualization.

## Architecture Overview

```
MagenticWorkflowApp/
├── Program.cs                     # Entry point + DI setup
├── Models/
│   └── WorkflowConfiguration.cs   # 7 config models
├── Services/
│   ├── Interfaces.cs              # Service contracts
│   ├── WorkflowJsonLoader.cs      # JSON loading + validation
│   ├── WorkflowVisualizer.cs      # Mermaid diagram generation
│   └── MagenticWorkflowOrchestrator.cs  # Main orchestrator
└── workflow-*.json                # Example configurations
```

## Coding Guidelines

### Naming Conventions
```csharp
// Classes: PascalCase
public class WorkflowOrchestrator { }

// Interfaces: I + PascalCase
public interface IWorkflowOrchestrator { }

// Methods: PascalCase
public async Task ExecuteWorkflowAsync() { }

// Private fields: _camelCase
private readonly ILogger _logger;

// Parameters: camelCase
public void Process(string workflowType) { }

// Constants: PascalCase
private const int MaxRoundCount = 50;
```

### Async/Await Patterns
```csharp
// ✅ DO: Use Async suffix
public async Task<WorkflowConfiguration> LoadConfigurationAsync(string path)

// ✅ DO: ConfigureAwait(false) in libraries
await File.ReadAllTextAsync(path).ConfigureAwait(false);

// ✅ DO: ValueTask for hot paths
public ValueTask<bool> ValidateAsync()

// ❌ DON'T: async void (except event handlers)
```

### Dependency Injection
```csharp
// ✅ DO: Constructor injection
public class WorkflowOrchestrator
{
    private readonly ILogger<WorkflowOrchestrator> _logger;
    private readonly IWorkflowJsonLoader _loader;
    
    public WorkflowOrchestrator(
        ILogger<WorkflowOrchestrator> logger,
        IWorkflowJsonLoader loader)
    {
        _logger = logger;
        _loader = loader;
    }
}

// ✅ DO: Register in Program.cs
services.AddSingleton<IWorkflowOrchestrator, MagenticWorkflowOrchestrator>();
```

### Error Handling
```csharp
// ✅ DO: Specific exceptions first
try {
    await orchestrator.ExecuteAsync();
}
catch (FileNotFoundException ex) {
    _logger.LogError(ex, "Config file not found: {Path}", path);
    throw new ConfigurationException("Configuration file missing", ex);
}
catch (JsonException ex) {
    _logger.LogError(ex, "Invalid JSON in: {Path}", path);
    throw new ConfigurationException("Invalid JSON format", ex);
}
catch (Exception ex) {
    _logger.LogError(ex, "Unexpected error in workflow execution");
    throw;
}

// ✅ DO: Use custom exceptions
public class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
}
```

### Logging Patterns
```csharp
// ✅ DO: Structured logging with parameters
_logger.LogInformation(
    "Executing {WorkflowType} workflow with {AgentCount} agents",
    config.WorkflowType,
    config.Agents.Count
);

// ✅ DO: Log levels appropriately
_logger.LogDebug("Loaded configuration: {@Config}", config);
_logger.LogInformation("Workflow started");
_logger.LogWarning("Agent {AgentName} took {Duration}ms", name, duration);
_logger.LogError(ex, "Workflow execution failed");

// ❌ DON'T: String interpolation in logs
_logger.LogInformation($"Processing {config.WorkflowType}"); // Wrong
```

## Workflow Type Implementations

### Pattern: Strategy per Type
```csharp
public async Task ExecuteWorkflowFromJsonAsync(string jsonFilePath)
{
    var config = await _jsonLoader.LoadConfigurationAsync(jsonFilePath);
    _visualizer.VisualizeWorkflow(config);
    
    // Strategy pattern based on workflow type
    return config.WorkflowType.ToLower() switch
    {
        "sequential" => await ExecuteSequentialWorkflowAsync(config),
        "concurrent" => await ExecuteConcurrentWorkflowAsync(config),
        "conditional" => await ExecuteConditionalWorkflowAsync(config),
        "magentic" => await ExecuteMagenticWorkflowAsync(config),
        _ => throw new NotSupportedException($"Workflow type '{config.WorkflowType}' not supported")
    };
}
```

### Sequential Implementation Pattern
```csharp
private async Task ExecuteSequentialWorkflowAsync(WorkflowConfiguration config)
{
    // 1. Create agents from config
    var agents = CreateAgentsFromConfiguration(config);
    
    // 2. Build workflow
    var builder = new WorkflowBuilder()
        .SetStartExecutor(agents[config.Orchestration!.StartAgent!]);
    
    foreach (var edge in config.Orchestration.Edges!)
    {
        builder.AddEdge(agents[edge.From], agents[edge.To]);
    }
    
    var workflow = builder.Build();
    
    // 3. Execute with event handling
    await foreach (var evt in workflow.RunStream(config.Task))
    {
        HandleWorkflowEvent(evt);
        
        if (evt is WorkflowCompletedEvent completed)
        {
            return completed;
        }
    }
}
```

### Agent Creation Pattern
```csharp
private Dictionary<string, ChatAgent> CreateAgentsFromConfiguration(
    WorkflowConfiguration config,
    string apiKey)
{
    var agents = new Dictionary<string, ChatAgent>();
    
    foreach (var agentConfig in config.Agents)
    {
        var chatClient = new OpenAIChatClient(
            aiModelId: agentConfig.ModelId,
            apiKey: apiKey
        );
        
        var tools = CreateTools(agentConfig.Tools);
        
        var agent = new ChatAgent(
            name: agentConfig.Name,
            description: agentConfig.Description,
            instructions: agentConfig.Instructions,
            chatClient: chatClient,
            tools: tools.ToArray()
        );
        
        agents.Add(agentConfig.Name, agent);
    }
    
    return agents;
}

private List<ITool> CreateTools(List<string> toolNames)
{
    var tools = new List<ITool>();
    
    foreach (var toolName in toolNames)
    {
        tools.Add(toolName switch
        {
            "CodeInterpreter" => new HostedCodeInterpreterTool(),
            _ => throw new NotSupportedException($"Tool '{toolName}' not supported")
        });
    }
    
    return tools;
}
```

## JSON Validation Patterns

```csharp
private void ValidateConfiguration(WorkflowConfiguration config)
{
    // Basic validation
    if (string.IsNullOrWhiteSpace(config.Task))
        throw new WorkflowValidationException("Task cannot be empty");
    
    if (config.Agents == null || config.Agents.Count == 0)
        throw new WorkflowValidationException("At least one agent must be configured");
    
    // Agent validation
    var agentNames = new HashSet<string>();
    foreach (var agent in config.Agents)
    {
        if (string.IsNullOrWhiteSpace(agent.Name))
            throw new WorkflowValidationException("Agent name cannot be empty");
        
        if (!agentNames.Add(agent.Name))
            throw new WorkflowValidationException($"Duplicate agent name: {agent.Name}");
        
        if (string.IsNullOrWhiteSpace(agent.Description))
            throw new WorkflowValidationException($"Agent '{agent.Name}' must have a description");
    }
    
    // Type-specific validation
    ValidateOrchestration(config, agentNames);
}

private void ValidateOrchestration(
    WorkflowConfiguration config,
    HashSet<string> agentNames)
{
    switch (config.WorkflowType.ToLower())
    {
        case "sequential":
            ValidateSequentialOrchestration(config, agentNames);
            break;
        case "concurrent":
            ValidateConcurrentOrchestration(config, agentNames);
            break;
        case "conditional":
            ValidateConditionalOrchestration(config, agentNames);
            break;
        case "magentic":
            ValidateMagenticConfiguration(config);
            break;
    }
}
```

## Mermaid Diagram Generation

```csharp
public string GenerateMermaidDiagram(WorkflowConfiguration config)
{
    var sb = new StringBuilder();
    sb.AppendLine("```mermaid");
    sb.AppendLine("graph TD");
    
    // Delegate to specific generator
    switch (config.WorkflowType.ToLower())
    {
        case "sequential":
            GenerateSequentialDiagram(sb, config);
            break;
        case "concurrent":
            GenerateConcurrentDiagram(sb, config);
            break;
        case "conditional":
            GenerateConditionalDiagram(sb, config);
            break;
        case "magentic":
            GenerateMagenticDiagram(sb, config);
            break;
    }
    
    sb.AppendLine("```");
    return sb.ToString();
}

private void GenerateSequentialDiagram(StringBuilder sb, WorkflowConfiguration config)
{
    sb.AppendLine("    Start([Task Start])");
    
    // Add agent nodes
    foreach (var agent in config.Agents)
    {
        var id = SanitizeId(agent.Name);
        sb.AppendLine($"    {id}[{agent.Name}]");
    }
    
    sb.AppendLine("    End([Final Result])");
    
    // Add edges
    var startId = SanitizeId(config.Orchestration!.StartAgent!);
    sb.AppendLine($"    Start --> {startId}");
    
    foreach (var edge in config.Orchestration.Edges!)
    {
        var fromId = SanitizeId(edge.From);
        var toId = SanitizeId(edge.To);
        var label = !string.IsNullOrEmpty(edge.Label) ? $"|{edge.Label}|" : "";
        sb.AppendLine($"    {fromId} -->{label} {toId}");
    }
    
    // Style nodes
    sb.AppendLine("    style Start fill:#9f9,stroke:#333");
    sb.AppendLine("    style End fill:#9f9,stroke:#333");
}

private string SanitizeId(string name)
{
    return name.Replace(" ", "_").Replace("-", "_");
}
```

## Console Output Patterns

```csharp
private void LogEvent(string source, string message, ConsoleColor color)
{
    Console.ForegroundColor = color;
    Console.Write($"[{source}] ");
    Console.ResetColor();
    Console.WriteLine(message);
}

private void ShowFinalResult(string message)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("\n" + new string('=', 60));
    Console.WriteLine("FINAL RESULT:");
    Console.WriteLine(new string('=', 60));
    Console.ResetColor();
    Console.WriteLine($"✅ {message}");
    Console.WriteLine(new string('=', 60));
}

// Event color mapping
private ConsoleColor GetEventColor(string eventType)
{
    return eventType switch
    {
        "ORCHESTRATOR" => ConsoleColor.Cyan,
        "AGENT" => ConsoleColor.Yellow,
        "DECISION" => ConsoleColor.Magenta,
        "WORKFLOW" => ConsoleColor.Blue,
        "ERROR" => ConsoleColor.Red,
        _ => ConsoleColor.White
    };
}
```

## Testing Patterns

### Unit Tests
```csharp
public class WorkflowJsonLoaderTests
{
    [Fact]
    public async Task LoadConfiguration_ValidFile_ReturnsConfiguration()
    {
        // Arrange
        var loader = new WorkflowJsonLoader(Mock.Of<ILogger<WorkflowJsonLoader>>());
        var path = "testdata/valid-config.json";
        
        // Act
        var config = await loader.LoadConfigurationAsync(path);
        
        // Assert
        Assert.NotNull(config);
        Assert.Equal("Sequential", config.WorkflowType);
        Assert.NotEmpty(config.Agents);
    }
    
    [Fact]
    public async Task LoadConfiguration_InvalidJson_ThrowsJsonException()
    {
        // Arrange
        var loader = new WorkflowJsonLoader(Mock.Of<ILogger<WorkflowJsonLoader>>());
        var path = "testdata/invalid.json";
        
        // Act & Assert
        await Assert.ThrowsAsync<JsonException>(
            () => loader.LoadConfigurationAsync(path)
        );
    }
}
```

### Integration Tests
```csharp
public class WorkflowIntegrationTests
{
    [Fact]
    public async Task ExecuteSequentialWorkflow_ValidConfig_CompletesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection();
        ConfigureTestServices(services);
        var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<IWorkflowOrchestrator>();
        
        // Act
        await orchestrator.ExecuteWorkflowFromJsonAsync("workflow-sequential.json");
        
        // Assert
        // Verify expected behavior
    }
}
```

## Common Patterns to Follow

### 1. Null Safety
```csharp
// ✅ DO: Use nullable reference types
public string? OptionalProperty { get; set; }

// ✅ DO: Null checks
if (config.Orchestration?.StartAgent == null)
    throw new WorkflowValidationException("StartAgent is required for Sequential workflow");

// ✅ DO: Null-coalescing
var modelId = agentConfig.ModelId ?? "gpt-4";
```

### 2. LINQ Usage
```csharp
// ✅ DO: Use LINQ for readability
var agentNames = config.Agents
    .Select(a => a.Name)
    .ToList();

// ✅ DO: FirstOrDefault with null check
var edge = config.Orchestration?.Edges?
    .FirstOrDefault(e => e.From == currentAgent);

if (edge != null)
{
    // Process edge
}
```

### 3. String Operations
```csharp
// ✅ DO: Case-insensitive comparisons
if (config.WorkflowType.Equals("Sequential", StringComparison.OrdinalIgnoreCase))

// ✅ DO: StringBuilder for concatenation in loops
var sb = new StringBuilder();
foreach (var line in lines)
{
    sb.AppendLine(line);
}
```

## Prohibited Patterns

### ❌ DON'T:
```csharp
// Don't use async void
async void ProcessWorkflow() { } // Wrong!

// Don't ignore exceptions
try {
    await workflow.ExecuteAsync();
} catch { } // Wrong!

// Don't hardcode configuration
const string ApiKey = "sk-..."; // Wrong!

// Don't use blocking calls on async
workflow.ExecuteAsync().Wait(); // Wrong!

// Don't mix concerns
public class WorkflowOrchestratorAndLoader { } // Wrong!
```

## File Templates

### New Service Interface
```csharp
namespace MagenticWorkflowApp.Services;

/// <summary>
/// Service for [description]
/// </summary>
public interface IMyService
{
    /// <summary>
    /// [Method description]
    /// </summary>
    Task<TResult> MethodAsync(TParameter parameter);
}
```

### New Service Implementation
```csharp
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Implementation of <see cref="IMyService"/>
/// </summary>
public class MyService : IMyService
{
    private readonly ILogger<MyService> _logger;
    
    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }
    
    public async Task<TResult> MethodAsync(TParameter parameter)
    {
        _logger.LogDebug("Method called with: {@Parameter}", parameter);
        
        try
        {
            // Implementation
            
            _logger.LogInformation("Method completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Method failed");
            throw;
        }
    }
}
```

## Quick Reference

### Workflow Types
- `Sequential`: Pipeline execution (A → B → C)
- `Concurrent`: Parallel execution ([A, B, C] → Aggregate)
- `Conditional`: Dynamic routing (A → Decision → [B|C|D])
- `Magentic`: Manager-coordinated (Manager ↔ [A, B, C])

### Key Interfaces
- `IWorkflowOrchestrator`: Main orchestrator
- `IWorkflowJsonLoader`: Configuration loading
- `IWorkflowVisualizer`: Diagram generation

### Key Models
- `WorkflowConfiguration`: Root config
- `OrchestrationConfiguration`: Orchestration details
- `AgentConfiguration`: Agent definition

## Remember

1. **One method to rule them all**: `ExecuteWorkflowFromJsonAsync()`
2. **Strategy per type**: Separate methods for each workflow type
3. **Validate early**: Check configuration before execution
4. **Visualize first**: Show diagram before running
5. **DEMO mode**: Simulate without API keys
6. **Log everything**: Structured logging everywhere
7. **Handle errors**: Try-catch with specific exceptions
8. **Use DI**: Constructor injection for all services

---

**Version:** 1.0  
**For:** GitHub Copilot, Cursor, other AI assistants  
**Updated:** 2024
