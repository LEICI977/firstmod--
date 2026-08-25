using System.Text.Json.Serialization;

namespace VivantValley;

/// <summary>Serializable, game-owned context passed to the LangGraph service.</summary>
public sealed class NpcContextSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string NpcName { get; set; } = string.Empty;
    public string NpcDisplayName { get; set; } = string.Empty;
    public string Identity { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public string Memory { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Goal { get; set; } = string.Empty;
    public string WorldState { get; set; } = string.Empty;
    public string PlayerProgress { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public string MemorySummary { get; set; } = string.Empty;
    /// <summary>Short-lived, game-confirmed shared experiences kept outside long-term memory.</summary>
    public IReadOnlyList<string> RecentSessionFacts { get; set; }
        = Array.Empty<string>();
    public IReadOnlyList<LangGraphConversationMessage> RecentMessages { get; set; }
        = Array.Empty<LangGraphConversationMessage>();
    public string NarrativeContext { get; set; } = string.Empty;
    /// <summary>Short, live scene observation around the NPC for grounded dialogue.</summary>
    public string SceneSnapshot { get; set; } = string.Empty;
    public string ActivitySummary { get; set; } = string.Empty;
    public IReadOnlyList<LangGraphGiftCandidate> AllowedTools { get; set; }
        = Array.Empty<LangGraphGiftCandidate>();
    public IReadOnlyList<LangGraphMoveDestination> AllowedMoveDestinations { get; set; }
        = Array.Empty<LangGraphMoveDestination>();
    public bool MineGuardAvailable { get; set; }
    public bool FishingCompanionAvailable { get; set; }
    public string PlayerInput { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public int Day { get; set; }
    public string Location { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ContextVersion { get; set; } = "1";
    public string Mode { get; set; } = "conversation";
    public Dictionary<string, string> RequestMetadata { get; set; }
        = new(StringComparer.Ordinal);
}

public sealed class LangGraphConversationMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string GameDate { get; set; } = string.Empty;
}

public sealed class LangGraphGiftCandidate
{
    public string CandidateKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DisplayHint { get; set; } = string.Empty;
}

public sealed class LangGraphMoveDestination
{
    public string DestinationKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Provider settings forwarded to the local graph service for one request.</summary>
public sealed class LangGraphLlmProfile
{
    public string Provider { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool EnableThinking { get; set; }
    public string ReasoningEffort { get; set; } = "low";
    /// <summary>Sampling temperature used by the LangGraph provider call.</summary>
    public double Temperature { get; set; } = 0.75d;
    /// <summary>Nucleus sampling probability used by the LangGraph provider call.</summary>
    public double TopP { get; set; } = 0.9d;
    public int MaxOutputTokens { get; set; } = 2048;
}

public sealed class LangGraphRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string NpcName { get; set; } = string.Empty;
    public int Day { get; set; }
    public string Location { get; set; } = string.Empty;
    public string ActionId { get; set; } = string.Empty;
    public string ContextVersion { get; set; } = "1";
    public string Mode { get; set; } = "conversation";
    public NpcContextSnapshot ContextSnapshot { get; set; } = new();
    public LangGraphLlmProfile Llm { get; set; } = new();
    public LangGraphBridgeAccess? GameBridge { get; set; }
}

public sealed class LangGraphDecision
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;
    public string Decision { get; set; } = "reply";
    public LangGraphAction Action { get; set; } = new();
    public string Reply { get; set; } = string.Empty;

    [JsonPropertyName("travel_barks")]
    public List<string> TravelBarks { get; set; } = new();

    [JsonPropertyName("memory_update")]
    public LangGraphMemoryUpdate MemoryUpdate { get; set; } = new();
}

public sealed class LangGraphAction
{
    public string Name { get; set; } = NpcGiftToolNames.None;

    [JsonPropertyName("candidate_key")]
    public string? CandidateKey { get; set; }

    [JsonPropertyName("destination_key")]
    public string? DestinationKey { get; set; }
    public string Delivery { get; set; } = SocialGiftDeliveryModes.Immediate;

    [JsonPropertyName("reason_tag")]
    public string ReasonTag { get; set; } = string.Empty;
}

public sealed class LangGraphMemoryUpdate
{
    [JsonPropertyName("summary_patch")]
    public string SummaryPatch { get; set; } = string.Empty;
    public LangGraphSignal Signal { get; set; } = new();
    public List<string> Topics { get; set; } = new();

    [JsonPropertyName("open_loops")]
    public List<string> OpenLoops { get; set; } = new();
}

public sealed class LangGraphSignal
{
    public double Valence { get; set; }
    public double Warmth { get; set; }
    public double Concern { get; set; }
    public double Confidence { get; set; }
}

public sealed class LangGraphResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string ContextVersion { get; set; } = string.Empty;
    public LangGraphDecision? Decision { get; set; }

    public LangGraphMoveConfirmation? Confirmation { get; set; }

    [JsonPropertyName("tool_execution")]
    public LangGraphToolExecution? ToolExecution { get; set; }
}

public sealed class LangGraphMoveConfirmation
{
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("resume_token")]
    public string ResumeToken { get; set; } = string.Empty;

    [JsonPropertyName("tool_call_id")]
    public string ToolCallId { get; set; } = string.Empty;

    [JsonPropertyName("destination_key")]
    public string DestinationKey { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("npc_display_name")]
    public string NpcDisplayName { get; set; } = string.Empty;
}

public sealed class LangGraphResumeRequest
{
    public string RequestId { get; set; } = string.Empty;

    public string ResumeToken { get; set; } = string.Empty;

    public bool Approved { get; set; }
}

public sealed class LangGraphToolExecution
{
    public string RequestId { get; set; } = string.Empty;

    public string ToolCallId { get; set; } = string.Empty;

    public string ContextVersion { get; set; } = string.Empty;

    public string Tool { get; set; } = NpcGiftToolNames.None;

    public string Status { get; set; } = "none";

    public bool Ok { get; set; }

    [JsonPropertyName("candidate_key")]
    public string? CandidateKey { get; set; }

    [JsonPropertyName("destination_key")]
    public string? DestinationKey { get; set; }

    public string? DisplayName { get; set; }

    public int Quantity { get; set; }

    [JsonPropertyName("reason_code")]
    public string? ReasonCode { get; set; }

    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("receipt_id")]
    public string ReceiptId { get; set; } = string.Empty;
}
