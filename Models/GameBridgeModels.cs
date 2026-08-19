using System.Text.Json.Serialization;

namespace VivantValley;

/// <summary>Short-lived access information for the loopback SMAPI game bridge.</summary>
public sealed class LangGraphBridgeAccess
{
    public string BaseUrl { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;
}

/// <summary>One provider tool call forwarded to the game-owned executor.</summary>
public sealed class GameBridgeToolRequest
{
    public string RequestId { get; set; } = string.Empty;

    public string ToolCallId { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;

    public string NpcName { get; set; } = string.Empty;

    public string ActionId { get; set; } = string.Empty;

    public string ContextVersion { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public string CandidateKey { get; set; } = string.Empty;

    public string ReasonTag { get; set; } = string.Empty;
}

/// <summary>Authoritative result returned by the SMAPI game bridge.</summary>
public sealed class GameBridgeToolResult
{
    public string RequestId { get; set; } = string.Empty;

    public string ToolCallId { get; set; } = string.Empty;

    public string ContextVersion { get; set; } = string.Empty;

    public string Tool { get; set; } = NpcGiftToolNames.None;

    public string Status { get; set; } = "rejected";

    public bool Ok { get; set; }

    [JsonPropertyName("candidate_key")]
    public string? CandidateKey { get; set; }

    public string? DisplayName { get; set; }

    public int Quantity { get; set; }

    public string? ReasonCode { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ReceiptId { get; set; } = string.Empty;
}
