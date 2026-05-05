using System.Text.Json;
using MagenticWorkflowApp.Exceptions;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Service for loading workflow configuration from JSON files
/// </summary>
public class WorkflowJsonLoader : IWorkflowJsonLoader
{
    private readonly ILogger<WorkflowJsonLoader> logger;
    private readonly JsonSerializerOptions jsonOptions;

    public WorkflowJsonLoader(ILogger<WorkflowJsonLoader> logger)
    {
        this.logger = logger;
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };
    }

    public async Task<WorkflowConfiguration> LoadConfigurationAsync(string jsonFilePath)
    {
        try
        {
            logger.LogInformation("Loading workflow configuration from: {FilePath}", jsonFilePath);

            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException($"Workflow configuration file not found: {jsonFilePath}");
            }

            var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
            
            Console.WriteLine($"[JSON Loader] Configuration file loaded, size: {jsonContent.Length} bytes");

            var configuration = JsonSerializer.Deserialize<WorkflowConfiguration>(jsonContent, jsonOptions);

            if (configuration == null)
            {
                throw new InvalidOperationException("Failed to deserialize workflow configuration");
            }

            // Apply environment variable substitution to MCP server fields
            ApplyEnvSubstitution(configuration);

            // Validate configuration
            ValidateConfiguration(configuration);

            // Validate MCP server configurations and references
            ValidateMcpServers(configuration);

            logger.LogInformation("Configuration loaded successfully:");
            logger.LogInformation("  - Workflow Type: {Type}", configuration.WorkflowType);
            logger.LogInformation("  - Agents Count: {Count}", configuration.Agents.Count);
            logger.LogInformation("  - Task: {Task}", 
                configuration.Task.Length > 100 
                    ? configuration.Task.Substring(0, 100) + "..." 
                    : configuration.Task);

            return configuration;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading workflow configuration");
            throw;
        }
    }

    private void ValidateConfiguration(WorkflowConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Task))
        {
            throw new InvalidOperationException("Task cannot be empty");
        }

        var isDeepResearch = string.Equals(configuration.WorkflowType, "deepResearch", StringComparison.OrdinalIgnoreCase);

        if (isDeepResearch)
        {
            ValidateDeepResearchConfiguration(configuration);
        }
        else
        {
            if (configuration.Agents == null || configuration.Agents.Count == 0)
            {
                throw new InvalidOperationException("At least one agent must be configured");
            }

            foreach (var agent in configuration.Agents)
            {
                if (string.IsNullOrWhiteSpace(agent.Name))
                {
                    throw new InvalidOperationException("Agent name cannot be empty");
                }

                if (string.IsNullOrWhiteSpace(agent.Description))
                {
                    throw new InvalidOperationException($"Agent '{agent.Name}' must have a description");
                }
            }
        }

        logger.LogInformation("Configuration validation passed");
    }

    private static void ValidateDeepResearchConfiguration(WorkflowConfiguration configuration)
    {
        if (configuration.DeepResearch is null)
        {
            throw new WorkflowValidationException(
                "deepResearch workflow requires a 'deepResearch' configuration section.");
        }

        var dr = configuration.DeepResearch;
        var roles = new (string RoleName, AgentConfiguration Cfg)[]
        {
            ("Clarifier",   dr.Clarifier),
            ("Planner",     dr.Planner),
            ("Researcher",  dr.Researcher),
            ("Critic",      dr.Critic),
            ("Synthesizer", dr.Synthesizer),
        };

        foreach (var (roleName, cfg) in roles)
        {
            if (cfg is null)
                throw new WorkflowValidationException($"deepResearch.{char.ToLower(roleName[0]) + roleName[1..]} is required.");
            if (string.IsNullOrWhiteSpace(cfg.Name))
                throw new WorkflowValidationException($"deepResearch role '{roleName}' must have a name.");
            if (string.IsNullOrWhiteSpace(cfg.Instructions))
                throw new WorkflowValidationException($"deepResearch role '{roleName}' must have instructions.");
            if (string.IsNullOrWhiteSpace(cfg.ModelId))
                throw new WorkflowValidationException($"deepResearch role '{roleName}' must specify a modelId.");
        }

        if (dr.MaxResearchIterations < 1 || dr.MaxResearchIterations > 10)
            throw new WorkflowValidationException("deepResearch.maxResearchIterations must be in [1..10].");
        if (dr.MaxParallelResearchers < 1 || dr.MaxParallelResearchers > 32)
            throw new WorkflowValidationException("deepResearch.maxParallelResearchers must be in [1..32].");
    }

    private static void ApplyEnvSubstitution(WorkflowConfiguration cfg)
    {
        foreach (var s in cfg.McpServers)
        {
            if (s.Command is not null)
            {
                s.Command = EnvVarSubstitution.Apply(s.Command);
            }

            for (int i = 0; i < s.Args.Count; i++)
            {
                s.Args[i] = EnvVarSubstitution.Apply(s.Args[i]);
            }

            foreach (var k in s.Env.Keys.ToList())
            {
                s.Env[k] = EnvVarSubstitution.Apply(s.Env[k]);
            }

            if (s.Url is not null)
            {
                s.Url = EnvVarSubstitution.Apply(s.Url);
            }

            foreach (var k in s.Headers.Keys.ToList())
            {
                s.Headers[k] = EnvVarSubstitution.Apply(s.Headers[k]);
            }
        }
    }

    private static void ValidateMcpServers(WorkflowConfiguration cfg)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in cfg.McpServers)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
            {
                throw new WorkflowValidationException("MCP server name cannot be empty");
            }

            if (!names.Add(s.Name))
            {
                throw new WorkflowValidationException($"Duplicate MCP server name: {s.Name}");
            }

            var t = s.Transport?.ToLowerInvariant();
            if (t != "stdio" && t != "http")
            {
                throw new WorkflowValidationException(
                    $"Unknown MCP transport '{s.Transport}' for server '{s.Name}'");
            }

            s.Transport = t!;

            if (t == "stdio" && string.IsNullOrWhiteSpace(s.Command))
            {
                throw new WorkflowValidationException(
                    $"MCP server '{s.Name}' (stdio) requires 'command'");
            }

            if (t == "http" && string.IsNullOrWhiteSpace(s.Url))
            {
                throw new WorkflowValidationException(
                    $"MCP server '{s.Name}' (http) requires 'url'");
            }
        }

        foreach (var agent in cfg.Agents)
        {
            foreach (var refName in agent.McpServers)
            {
                if (!names.Contains(refName))
                {
                    throw new WorkflowValidationException(
                        $"Agent '{agent.Name}' references unknown MCP server '{refName}'");
                }
            }
        }
    }
}
