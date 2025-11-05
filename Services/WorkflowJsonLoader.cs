using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
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

            // Validate configuration
            ValidateConfiguration(configuration);

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
}
