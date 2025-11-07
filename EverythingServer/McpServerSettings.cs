namespace EverythingServer;

/// <summary>
/// Application configuration for the MCP server.
/// </summary>
public class McpServerSettings
{
    /// <summary>
    /// When set to <see langword="true"/>, the server will run in stateless mode and no
    /// <c>Mcp-Session-Id</c> header will be required.
    /// </summary>
    public bool Stateless { get; set; }
}

