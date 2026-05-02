namespace MagenticWorkflowApp.Exceptions;

public sealed class McpServerCommunicationException : Exception
{
    public string ServerName { get; }

    public McpServerCommunicationException(string serverName, string message, Exception? inner = null)
        : base($"MCP server '{serverName}' communication failure: {message}", inner)
    {
        ServerName = serverName;
    }
}
