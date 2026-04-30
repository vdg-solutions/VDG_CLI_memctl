using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memctl.Boundary.Mcp;

public sealed class McpResponse<TResult>
{
    [JsonPropertyName("jsonrpc")] public string      Jsonrpc { get; init; } = "2.0";
    [JsonPropertyName("id")]      public JsonElement Id      { get; init; }
    [JsonPropertyName("result")]  public TResult?    Result  { get; init; }
}

public sealed class McpErrorResponse
{
    [JsonPropertyName("jsonrpc")] public string      Jsonrpc { get; init; } = "2.0";
    [JsonPropertyName("id")]      public JsonElement Id      { get; init; }
    [JsonPropertyName("error")]   public McpError    Error   { get; init; } = new();
}

public sealed class McpError
{
    [JsonPropertyName("code")]    public int    Code    { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = "";
}

public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")] public string             ProtocolVersion { get; init; } = "";
    [JsonPropertyName("capabilities")]    public ServerCapabilities Capabilities    { get; init; } = new();
    [JsonPropertyName("serverInfo")]      public ServerInfo         ServerInfo      { get; init; } = new();
}

public sealed class ServerCapabilities
{
    [JsonPropertyName("tools")] public ToolsCapability Tools { get; init; } = new();
}

public sealed class ToolsCapability { }

public sealed class ServerInfo
{
    [JsonPropertyName("name")]         public string Name         { get; init; } = "";
    [JsonPropertyName("version")]      public string Version      { get; init; } = "";
    [JsonPropertyName("instructions")] public string Instructions { get; init; } = "";
}

public sealed class ToolsListResult
{
    [JsonPropertyName("tools")] public ToolDef[] Tools { get; init; } = [];
}

public sealed class ToolDef
{
    [JsonPropertyName("name")]         public string        Name         { get; init; } = "";
    [JsonPropertyName("description")]  public string        Description  { get; init; } = "";
    [JsonPropertyName("inputSchema")]  public InputSchema   InputSchema  { get; init; } = new();
    [JsonPropertyName("outputSchema")] public OutputSchema  OutputSchema { get; init; } = new();
}

public sealed class InputSchema
{
    [JsonPropertyName("type")]       public string                          Type       { get; init; } = "object";
    [JsonPropertyName("properties")] public Dictionary<string, PropertySpec> Properties { get; init; } = new();
    [JsonPropertyName("required")]   public string[]                        Required   { get; init; } = [];
}

public sealed class PropertySpec
{
    [JsonPropertyName("type")]        public string Type        { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
}

public sealed class OutputSchema
{
    [JsonPropertyName("type")]       public string                  Type       { get; init; } = "object";
    [JsonPropertyName("properties")] public OutputSchemaProperties  Properties { get; init; } = new();
    [JsonPropertyName("required")]   public string[]                Required   { get; init; } = ["success", "action", "message"];
}

public sealed class OutputSchemaProperties
{
    [JsonPropertyName("success")] public PropertySpec     Success { get; init; } = new() { Type = "boolean", Description = "True if the operation succeeded" };
    [JsonPropertyName("action")]  public PropertySpec     Action  { get; init; } = new() { Type = "string",  Description = "Echoed action name matching the tool" };
    [JsonPropertyName("message")] public PropertySpec     Message { get; init; } = new() { Type = "string",  Description = "Human-readable status message" };
    [JsonPropertyName("data")]    public DataPropertySpec Data    { get; init; } = new();
}

public sealed class DataPropertySpec
{
    [JsonPropertyName("type")]        public string[] Type        { get; init; } = ["object", "null"];
    [JsonPropertyName("description")] public string   Description { get; init; } = "No payload";
}

public sealed class ToolCallResult
{
    [JsonPropertyName("content")] public ToolContent[] Content { get; init; } = [];
    [JsonPropertyName("isError")] public bool          IsError { get; init; }
}

public sealed class ToolContent
{
    [JsonPropertyName("type")] public string Type { get; init; } = "text";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
}

public sealed class EmptyResult { }
