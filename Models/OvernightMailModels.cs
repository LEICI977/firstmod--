using System.Text.Json.Serialization;

namespace VivantValley;

/// <summary>A bounded transcript entry used only for end-of-day surprise-mail planning.</summary>
public sealed class DailyConversationJournalEntry
{
    public int Day { get; set; } = -1;

    public string NpcName { get; set; } = string.Empty;

    public string NpcDisplayName { get; set; } = string.Empty;

    public long ConversationTurn { get; set; }

    public string PlayerExcerpt { get; set; } = string.Empty;

    public string NpcExcerpt { get; set; } = string.Empty;

    /// <summary>True for a controller-mode NPC-initiated encounter rather than a manual AI chat.</summary>
    public bool IsProactiveEncounter { get; set; }

    /// <summary>Persisted result of the controller proactive-mail probability roll.</summary>
    public bool PassedMailChance { get; set; }

    public void Normalize()
    {
        Day = Math.Max(-1, Day);
        NpcName = SocialModelNormalization.LimitSingleLine(NpcName, 80);
        NpcDisplayName = SocialModelNormalization.LimitSingleLine(NpcDisplayName, 80);
        ConversationTurn = Math.Max(0, ConversationTurn);
        PlayerExcerpt = LimitText(PlayerExcerpt, 600);
        NpcExcerpt = LimitText(NpcExcerpt, 800);
    }

    private static string LimitText(string? value, int maximumCharacters)
    {
        string text = (value ?? string.Empty).Replace('\r', ' ').Trim();
        return text.Length <= maximumCharacters ? text : text[..maximumCharacters] + "…";
    }
}

/// <summary>
/// Persisted work item prepared before the overnight save. It contains no item IDs,
/// so an interrupted API call can be safely restarted after loading the save.
/// </summary>
public sealed class OvernightMailPlanSnapshot
{
    public string PlanId { get; set; } = string.Empty;

    public int SourceDay { get; set; } = -1;

    public int DeliverOnOrAfterDay { get; set; } = -1;

    public int AttemptCount { get; set; }

    public List<OvernightMailNpcSnapshot> Npcs { get; set; } = new();

    public void Normalize()
    {
        PlanId = SocialModelNormalization.LimitSingleLine(PlanId, 128);
        SourceDay = Math.Max(-1, SourceDay);
        DeliverOnOrAfterDay = Math.Max(SourceDay + 1, DeliverOnOrAfterDay);
        AttemptCount = Math.Clamp(AttemptCount, 0, 20);
        Npcs = (Npcs ?? new List<OvernightMailNpcSnapshot>())
            .Where(value => value is not null)
            .Select(value =>
            {
                value.Normalize();
                return value;
            })
            .Where(value => value.NpcName.Length > 0
                            && value.ActionId.Length > 0
                            && value.GiftCandidates.Count > 0)
            .GroupBy(value => value.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(16)
            .ToList();
    }
}

public sealed class OvernightMailNpcSnapshot
{
    public string ActionId { get; set; } = string.Empty;

    public string NpcName { get; set; } = string.Empty;

    public string NpcDisplayName { get; set; } = string.Empty;

    public string GameContext { get; set; } = string.Empty;

    public string ConversationExcerpt { get; set; } = string.Empty;

    public string SignalSummary { get; set; } = string.Empty;

    public string ActivitySummary { get; set; } = string.Empty;

    public List<OvernightMailGiftOption> GiftCandidates { get; set; } = new();

    public void Normalize()
    {
        ActionId = SocialModelNormalization.LimitSingleLine(ActionId, 128);
        NpcName = SocialModelNormalization.LimitSingleLine(NpcName, 80);
        NpcDisplayName = SocialModelNormalization.LimitSingleLine(NpcDisplayName, 80);
        GameContext = LimitText(GameContext, 1200);
        ConversationExcerpt = LimitText(ConversationExcerpt, 1800);
        SignalSummary = LimitText(SignalSummary, 700);
        ActivitySummary = LimitText(ActivitySummary, 800);
        GiftCandidates = (GiftCandidates ?? new List<OvernightMailGiftOption>())
            .Where(value => value is not null)
            .Select(value =>
            {
                value.Normalize();
                return value;
            })
            .Where(value => value.CandidateId.Length > 0 && value.DisplayName.Length > 0)
            .GroupBy(value => value.CandidateId, StringComparer.Ordinal)
            .Select(group => group.First())
            .Take(8)
            .ToList();
    }

    private static string LimitText(string? value, int maximumCharacters)
    {
        string text = (value ?? string.Empty).Replace('\r', ' ').Trim();
        return text.Length <= maximumCharacters ? text : text[..maximumCharacters] + "…";
    }
}

public sealed class OvernightMailGiftOption
{
    public string CandidateId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public List<string> ReasonTags { get; set; } = new();

    public string Hint { get; set; } = string.Empty;

    public void Normalize()
    {
        CandidateId = SocialModelNormalization.LimitSingleLine(CandidateId, 64);
        DisplayName = SocialModelNormalization.LimitSingleLine(DisplayName, 120);
        ReasonTags = SocialModelNormalization.NormalizeTokens(ReasonTags, 8, 64);
        Hint = SocialModelNormalization.LimitSingleLine(Hint, 240);
    }
}

public sealed class OvernightMailPlanRequest
{
    public int SourceDay { get; init; }

    public int MaximumGiftCount { get; init; } = 2;

    public IReadOnlyList<OvernightMailNpcSnapshot> Npcs { get; init; }
        = Array.Empty<OvernightMailNpcSnapshot>();

    public string Model { get; init; } = "deepseek-v4-flash";

    public string ThinkingType { get; init; } = "disabled";

    public string ReasoningEffort { get; init; } = "low";

    public int MaxOutputTokens { get; init; } = 900;
}

public sealed class OvernightMailPlanDecision
{
    public IReadOnlyList<OvernightMailGiftDecision> Gifts { get; init; }
        = Array.Empty<OvernightMailGiftDecision>();

    public bool UsedFallback { get; init; }

    public string FailureReason { get; init; } = string.Empty;
}

public sealed class OvernightMailGiftDecision
{
    public string NpcName { get; init; } = string.Empty;

    public string GiftCandidateId { get; init; } = string.Empty;

    public string ReasonTag { get; init; } = string.Empty;

    /// <summary>Plain personal note. Item identity is appended later by game-side code.</summary>
    public string LetterBody { get; init; } = string.Empty;
}

internal sealed class OvernightMailWireResponse
{
    [JsonPropertyName("gifts")]
    public List<OvernightMailWireGift>? Gifts { get; set; }
}

internal sealed class OvernightMailWireGift
{
    [JsonPropertyName("npcName")]
    public string? NpcName { get; set; }

    [JsonPropertyName("giftCandidateId")]
    public string? GiftCandidateId { get; set; }

    [JsonPropertyName("reasonTag")]
    public string? ReasonTag { get; set; }

    [JsonPropertyName("letterBody")]
    public string? LetterBody { get; set; }
}
