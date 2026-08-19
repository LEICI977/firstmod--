using System.Text.Json.Serialization;

namespace VivantValley;

public static class SocialSceneActions
{
    public const string TalkOnly = "talk_only";
    public const string Gift = "gift";
}

/// <summary>A safe, code-selected gift option exposed to the model by opaque ID.</summary>
public sealed record SocialSceneGiftOption(
    string CandidateId,
    string DisplayName,
    IReadOnlyList<string> ReasonTags,
    string Hint = "");

public sealed class AiSocialSceneRequest
{
    public string NpcName { get; init; } = string.Empty;

    public string NpcDisplayName { get; init; } = string.Empty;

    public string GameContext { get; init; } = string.Empty;

    public string RecentConversation { get; init; } = string.Empty;

    public string SignalSummary { get; init; } = string.Empty;

    public string ActivitySummary { get; init; } = string.Empty;

    public IReadOnlyList<SocialSceneGiftOption> GiftCandidates { get; init; }
        = Array.Empty<SocialSceneGiftOption>();

    public string FallbackDialogue { get; init; } = string.Empty;

    public string Model { get; init; } = "deepseek-v4-flash";

    public string ThinkingType { get; init; } = "disabled";

    public string ReasoningEffort { get; init; } = "low";

    public int MaxOutputTokens { get; init; } = 600;

    public int MaxDialogueCharacters { get; init; } = 420;
}

public sealed class AiSocialSceneDecision
{
    public string Dialogue { get; init; } = string.Empty;

    public string Action { get; init; } = SocialSceneActions.TalkOnly;

    public string? GiftCandidateId { get; init; }

    public string MotiveTag { get; init; } = string.Empty;

    public bool UsedFallback { get; init; }

    public string FailureReason { get; init; } = string.Empty;
}

internal sealed class AiSocialSceneWireResponse
{
    [JsonPropertyName("dialogue")]
    public string? Dialogue { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("giftCandidateId")]
    public string? GiftCandidateId { get; set; }

    [JsonPropertyName("motiveTag")]
    public string? MotiveTag { get; set; }
}
