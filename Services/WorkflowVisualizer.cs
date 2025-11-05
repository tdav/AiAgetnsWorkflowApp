using System;
using System.Linq;
using System.Text;
using MagenticWorkflowApp.Interfaces;
using MagenticWorkflowApp.Models;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Services;

/// <summary>
/// Service for visualizing workflow structure
/// </summary>
public class WorkflowVisualizer : IWorkflowVisualizer
{
    private readonly ILogger<WorkflowVisualizer> _logger;

    public WorkflowVisualizer(ILogger<WorkflowVisualizer> logger)
    {
        _logger = logger;
    }

    public void VisualizeWorkflow(WorkflowConfiguration configuration)
    {
        Console.WriteLine("\n" + new string('═', 80));
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("WORKFLOW VISUALIZATION");
        Console.ResetColor();
        Console.WriteLine(new string('═', 80));

        // Display workflow type
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n📋 Workflow Type: {configuration.WorkflowType}");
        Console.ResetColor();

        // Display task
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n🎯 Task:");
        Console.ResetColor();
        Console.WriteLine($"   {WrapText(configuration.Task, 75, 3)}");

        // Display manager configuration
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"\n⚙️  Manager Configuration:");
        Console.ResetColor();
        Console.WriteLine($"   Model: {configuration.Manager.ModelId}");
        Console.WriteLine($"   Max Rounds: {configuration.Manager.MaxRoundCount}");
        Console.WriteLine($"   Max Stalls: {configuration.Manager.MaxStallCount}");
        Console.WriteLine($"   Max Resets: {configuration.Manager.MaxResetCount}");
        Console.WriteLine($"   Plan Review: {(configuration.Manager.EnablePlanReview ? "Enabled" : "Disabled")}");

        // Display agents
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"\n👥 Agents ({configuration.Agents.Count}):");
        Console.ResetColor();

        for (int i = 0; i < configuration.Agents.Count; i++)
        {
            var agent = configuration.Agents[i];
            Console.WriteLine($"\n   [{i + 1}] {agent.Name}");
            Console.WriteLine($"       Model: {agent.ModelId}");
            Console.WriteLine($"       Description: {agent.Description}");
            
            if (!string.IsNullOrWhiteSpace(agent.Instructions))
            {
                Console.WriteLine($"       Instructions: {WrapText(agent.Instructions, 70, 7)}");
            }

            if (agent.Tools.Any())
            {
                Console.WriteLine($"       Tools: {string.Join(", ", agent.Tools)}");
            }
        }

        // Display orchestration details
        if (configuration.Orchestration != null)
        {
            DisplayOrchestrationDetails(configuration);
        }

        // Generate Mermaid diagram
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n📊 Mermaid Diagram:");
        Console.ResetColor();
        var mermaidDiagram = GenerateMermaidDiagram(configuration);
        Console.WriteLine(mermaidDiagram);

        Console.WriteLine("\n" + new string('═', 80) + "\n");
    }

    private void DisplayOrchestrationDetails(WorkflowConfiguration config)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"\n🔗 Orchestration Details:");
        Console.ResetColor();

        switch (config.WorkflowType.ToLower())
        {
            case "sequential":
                DisplaySequentialDetails(config.Orchestration!);
                break;
            case "concurrent":
                DisplayConcurrentDetails(config.Orchestration!);
                break;
            case "conditional":
                DisplayConditionalDetails(config.Orchestration!);
                break;
        }
    }

    private void DisplaySequentialDetails(OrchestrationConfiguration orch)
    {
        Console.WriteLine($"   Pattern: Sequential Pipeline");
        if (!string.IsNullOrEmpty(orch.StartAgent))
        {
            Console.WriteLine($"   Start Agent: {orch.StartAgent}");
        }
        
        if (orch.Edges.Any())
        {
            Console.WriteLine($"   Execution Flow:");
            foreach (var edge in orch.Edges)
            {
                var label = !string.IsNullOrEmpty(edge.Label) ? $" [{edge.Label}]" : "";
                Console.WriteLine($"      {edge.From} → {edge.To}{label}");
            }
        }
    }

    private void DisplayConcurrentDetails(OrchestrationConfiguration orch)
    {
        Console.WriteLine($"   Pattern: Concurrent Fan-out/Fan-in");
        if (orch.Concurrent != null)
        {
            Console.WriteLine($"   Participants: {string.Join(", ", orch.Concurrent.ParticipantAgents)}");
            Console.WriteLine($"   Aggregation: {orch.Concurrent.AggregationStrategy}");
            Console.WriteLine($"   Execution: All agents run in parallel");
        }
    }

    private void DisplayConditionalDetails(OrchestrationConfiguration orch)
    {
        Console.WriteLine($"   Pattern: Conditional with Dynamic Routing");
        if (!string.IsNullOrEmpty(orch.StartAgent))
        {
            Console.WriteLine($"   Start Agent: {orch.StartAgent}");
        }
        
        if (orch.Edges.Any())
        {
            Console.WriteLine($"   Static Edges:");
            foreach (var edge in orch.Edges)
            {
                Console.WriteLine($"      {edge.From} → {edge.To}");
            }
        }
        
        if (orch.ConditionalEdges.Any())
        {
            Console.WriteLine($"   Conditional Edges:");
            foreach (var condEdge in orch.ConditionalEdges)
            {
                Console.WriteLine($"      From: {condEdge.From}");
                Console.WriteLine($"      Condition: {condEdge.SelectionFunction}");
                Console.WriteLine($"      Options: {string.Join(", ", condEdge.ToOptions)}");
            }
        }
    }

    public string GenerateMermaidDiagram(WorkflowConfiguration configuration)
    {
        var sb = new StringBuilder();
        sb.AppendLine("```mermaid");
        sb.AppendLine("graph TD");
        
        switch (configuration.WorkflowType.ToLower())
        {
            case "sequential":
                GenerateSequentialDiagram(sb, configuration);
                break;
            case "concurrent":
                GenerateConcurrentDiagram(sb, configuration);
                break;
            case "conditional":
                GenerateConditionalDiagram(sb, configuration);
                break;
            case "magentic":
                GenerateMagenticDiagram(sb, configuration);
                break;
        }
        
        sb.AppendLine("```");
        
        return sb.ToString();
    }

    private void GenerateSequentialDiagram(StringBuilder sb, WorkflowConfiguration config)
    {
        sb.AppendLine("    Start([Task Start])");
        
        // Add all agents as nodes
        foreach (var agent in config.Agents)
        {
            var agentId = agent.Name.Replace(" ", "_");
            sb.AppendLine($"    {agentId}[{agent.Name}<br/>{agent.Description}]");
        }
        
        sb.AppendLine("    End([Final Result])");
        
        // Connect start to first agent
        if (config.Orchestration?.StartAgent != null)
        {
            var startId = config.Orchestration.StartAgent.Replace(" ", "_");
            sb.AppendLine($"    Start --> {startId}");
        }
        else if (config.Agents.Any())
        {
            var firstId = config.Agents.First().Name.Replace(" ", "_");
            sb.AppendLine($"    Start --> {firstId}");
        }
        
        // Add edges between agents
        if (config.Orchestration?.Edges != null)
        {
            foreach (var edge in config.Orchestration.Edges)
            {
                var fromId = edge.From.Replace(" ", "_");
                var toId = edge.To.Replace(" ", "_");
                var label = !string.IsNullOrEmpty(edge.Label) ? $"|{edge.Label}|" : "";
                sb.AppendLine($"    {fromId} -->{label} {toId}");
            }
            
            // Connect last agent to end
            var lastEdge = config.Orchestration.Edges.LastOrDefault();
            if (lastEdge != null)
            {
                var lastId = lastEdge.To.Replace(" ", "_");
                sb.AppendLine($"    {lastId} --> End");
            }
        }
        
        // Styling
        sb.AppendLine("    style Start fill:#9f9,stroke:#333,stroke-width:2px");
        sb.AppendLine("    style End fill:#9f9,stroke:#333,stroke-width:2px");
    }

    private void GenerateConcurrentDiagram(StringBuilder sb, WorkflowConfiguration config)
    {
        sb.AppendLine("    Start([Task Start])");
        sb.AppendLine("    FanOut{Fan-Out}");
        sb.AppendLine("    FanIn{Fan-In}");
        sb.AppendLine("    End([Final Result])");
        
        sb.AppendLine("    Start --> FanOut");
        
        var participants = config.Orchestration?.Concurrent?.ParticipantAgents ?? 
                          config.Agents.Select(a => a.Name).ToList();
        
        foreach (var agentName in participants)
        {
            var agent = config.Agents.FirstOrDefault(a => a.Name == agentName);
            if (agent != null)
            {
                var agentId = agent.Name.Replace(" ", "_");
                sb.AppendLine($"    {agentId}[{agent.Name}]");
                sb.AppendLine($"    FanOut -.->|Parallel| {agentId}");
                sb.AppendLine($"    {agentId} -.-> FanIn");
            }
        }
        
        sb.AppendLine("    FanIn --> End");
        
        var strategy = config.Orchestration?.Concurrent?.AggregationStrategy ?? "Collect";
        
        // Styling
        sb.AppendLine("    style Start fill:#9f9,stroke:#333,stroke-width:2px");
        sb.AppendLine("    style End fill:#9f9,stroke:#333,stroke-width:2px");
        sb.AppendLine("    style FanOut fill:#ff9,stroke:#333,stroke-width:3px");
        sb.AppendLine($"    style FanIn fill:#ff9,stroke:#333,stroke-width:3px");
        
        foreach (var agentName in participants)
        {
            var agentId = agentName.Replace(" ", "_");
            sb.AppendLine($"    style {agentId} fill:#9cf,stroke:#333,stroke-width:2px");
        }
    }

    private void GenerateConditionalDiagram(StringBuilder sb, WorkflowConfiguration config)
    {
        sb.AppendLine("    Start([Task Start])");
        
        // Add all agents as nodes
        foreach (var agent in config.Agents)
        {
            var agentId = agent.Name.Replace(" ", "_");
            sb.AppendLine($"    {agentId}[{agent.Name}]");
        }
        
        sb.AppendLine("    End([Final Result])");
        
        // Connect start
        if (config.Orchestration?.StartAgent != null)
        {
            var startId = config.Orchestration.StartAgent.Replace(" ", "_");
            sb.AppendLine($"    Start --> {startId}");
        }
        
        // Add regular edges
        if (config.Orchestration?.Edges != null)
        {
            foreach (var edge in config.Orchestration.Edges)
            {
                var fromId = edge.From.Replace(" ", "_");
                var toId = edge.To.Replace(" ", "_");
                sb.AppendLine($"    {fromId} --> {toId}");
            }
        }
        
        // Add conditional edges
        if (config.Orchestration?.ConditionalEdges != null)
        {
            foreach (var condEdge in config.Orchestration.ConditionalEdges)
            {
                var fromId = condEdge.From.Replace(" ", "_");
                var decisionId = $"Decision_{fromId}";
                
                sb.AppendLine($"    {decisionId}{{{{Condition<br/>{condEdge.SelectionFunction}}}}}");
                sb.AppendLine($"    {fromId} --> {decisionId}");
                
                foreach (var target in condEdge.ToOptions)
                {
                    var targetId = target.Replace(" ", "_");
                    sb.AppendLine($"    {decisionId} -.->|Option| {targetId}");
                }
            }
        }
        
        // Find terminal nodes and connect to End
        var allTargets = new HashSet<string>();
        if (config.Orchestration?.Edges != null)
        {
            foreach (var edge in config.Orchestration.Edges)
            {
                allTargets.Add(edge.To);
            }
        }
        if (config.Orchestration?.ConditionalEdges != null)
        {
            foreach (var condEdge in config.Orchestration.ConditionalEdges)
            {
                foreach (var target in condEdge.ToOptions)
                {
                    allTargets.Add(target);
                }
            }
        }
        
        var allSources = new HashSet<string>();
        if (config.Orchestration?.Edges != null)
        {
            foreach (var edge in config.Orchestration.Edges)
            {
                allSources.Add(edge.From);
            }
        }
        if (config.Orchestration?.ConditionalEdges != null)
        {
            foreach (var condEdge in config.Orchestration.ConditionalEdges)
            {
                allSources.Add(condEdge.From);
            }
        }
        
        var terminalNodes = allTargets.Except(allSources).ToList();
        foreach (var terminal in terminalNodes)
        {
            var terminalId = terminal.Replace(" ", "_");
            sb.AppendLine($"    {terminalId} --> End");
        }
        
        // Styling
        sb.AppendLine("    style Start fill:#9f9,stroke:#333,stroke-width:2px");
        sb.AppendLine("    style End fill:#9f9,stroke:#333,stroke-width:2px");
        
        if (config.Orchestration?.ConditionalEdges != null)
        {
            foreach (var condEdge in config.Orchestration.ConditionalEdges)
            {
                var fromId = condEdge.From.Replace(" ", "_");
                var decisionId = $"Decision_{fromId}";
                sb.AppendLine($"    style {decisionId} fill:#fc9,stroke:#333,stroke-width:3px");
            }
        }
    }

    private void GenerateMagenticDiagram(StringBuilder sb, WorkflowConfiguration config)
    {
        sb.AppendLine("    Start([Task Start]) --> Manager[Magentic Manager]");
        
        foreach (var agent in config.Agents)
        {
            var agentId = agent.Name.Replace(" ", "_");
            sb.AppendLine($"    Manager -->|Delegates| {agentId}[{agent.Name}]");
            sb.AppendLine($"    {agentId} -->|Returns Result| Manager");
        }
        
        sb.AppendLine("    Manager --> End([Final Result])");
        sb.AppendLine("    ");
        sb.AppendLine("    style Manager fill:#f9f,stroke:#333,stroke-width:4px");
        sb.AppendLine("    style Start fill:#9f9,stroke:#333,stroke-width:2px");
        sb.AppendLine("    style End fill:#9f9,stroke:#333,stroke-width:2px");
    }

    private string WrapText(string text, int maxWidth, int indent)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxWidth)
            return text;

        var words = text.Split(' ');
        var lines = new StringBuilder();
        var currentLine = new StringBuilder();
        var indentStr = new string(' ', indent);

        foreach (var word in words)
        {
            if (currentLine.Length + word.Length + 1 > maxWidth)
            {
                lines.AppendLine(currentLine.ToString());
                currentLine.Clear();
                currentLine.Append(indentStr);
            }

            if (currentLine.Length > indent)
                currentLine.Append(' ');

            currentLine.Append(word);
        }

        if (currentLine.Length > indent)
            lines.Append(currentLine.ToString());

        return lines.ToString().TrimEnd();
    }
}
