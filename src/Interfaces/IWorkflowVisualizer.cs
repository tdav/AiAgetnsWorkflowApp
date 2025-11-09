using System.Threading.Tasks;
using MagenticWorkflowApp.Models;

namespace MagenticWorkflowApp.Interfaces;

/// <summary>
/// Interface for workflow visualization
/// </summary>
public interface IWorkflowVisualizer
{
    void VisualizeWorkflow(WorkflowConfiguration configuration);
    string GenerateMermaidDiagram(WorkflowConfiguration configuration);
}
