using System.Text.Json.Serialization;

namespace Memctl.Boundary.Llm;

public sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]           public string                 Model          { get; init; } = "";
    [JsonPropertyName("messages")]        public OpenAiChatMessage[]    Messages       { get; init; } = [];
    [JsonPropertyName("response_format")] public OpenAiResponseFormat?  ResponseFormat { get; init; }
    [JsonPropertyName("max_tokens")]      public int                    MaxTokens      { get; init; }
}

public sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]    public string Role    { get; init; } = "";
    [JsonPropertyName("content")] public string Content { get; init; } = "";
}

public sealed class OpenAiResponseFormat
{
    [JsonPropertyName("type")] public string Type { get; init; } = "json_object";
}
