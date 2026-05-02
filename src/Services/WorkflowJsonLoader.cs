using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
    private readonly ILogger<WorkflowJsonLoader> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public WorkflowJsonLoader(ILogger<WorkflowJsonLoader> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
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
            _logger.LogInformation("Loading workflow configuration from: {FilePath}", jsonFilePath);

            if (!File.Exists(jsonFilePath))
            {
                throw new FileNotFoundException($"Workflow configuration file not found: {jsonFilePath}");
            }

            var jsonContent = await File.ReadAllTextAsync(jsonFilePath);
            
            Console.WriteLine($"[JSON Loader] Configuration file loaded, size: {jsonContent.Length} bytes");

            var configuration = JsonSerializer.Deserialize<WorkflowConfiguration>(jsonContent, _jsonOptions);

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

            _logger.LogInformation("Configuration loaded successfully:");
            _logger.LogInformation("  - Workflow Type: {Type}", configuration.WorkflowType);
            _logger.LogInformation("  - Agents Count: {Count}", configuration.Agents.Count);
            _logger.LogInformation("  - Task: {Task}", 
                configuration.Task.Length > 100 
                    ? configuration.Task.Substring(0, 100) + "..." 
                    : configuration.Task);

            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading workflow configuration");
            throw;
        }
    }

    private void ValidateConfiguration(WorkflowConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Task))
        {
            throw new InvalidOperationException("Task cannot be empty");
        }

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

        _logger.LogInformation("Configuration validation passed");
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
