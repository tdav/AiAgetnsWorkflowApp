namespace MagenticWorkflowApp.Exceptions;

public sealed class McpServerStartupException : Exception
{
    public string ServerName { get; }

    public McpServerStartupException(string serverName, string message, Exception? inner = null)
        : base($"MCP server '{serverName}' failed to start: {message}", inner)
    {
        ServerName = serverName;
    }
}
