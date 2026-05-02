namespace MagenticWorkflowApp.Exceptions;

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
    public WorkflowValidationException(string message, Exception inner) : base(message, inner) { }
}
