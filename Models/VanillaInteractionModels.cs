namespace VivantValley;

public static class ConversationMemorySources
{
    public const string AiChat = "ai_chat";
    public const string VanillaDialogue = "vanilla_dialogue";
    public const string VanillaChoice = "vanilla_choice";
    public const string VanillaGift = "vanilla_gift";
    public const string VanillaEvent = "vanilla_event";
    public const string ModGift = "mod_gift";
    public const string ModProactive = "mod_proactive";
    public const string ModSocial = "mod_social";
    public const string ModMail = "mod_mail";
}

public static class NarrativeBeatKinds
{
    public const string NpcDialogue = "npc_dialogue";
    public const string Question = "question";
    public const string PlayerChoice = "player_choice";
    public const string Gift = "gift";
}

/// <summary>A complete, separately persisted vanilla event which is never folded into rolling chat compaction.</summary>
public sealed class NarrativeEpisode
{
    public string EpisodeId { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string GameDate { get; set; } = string.Empty;

    public int TotalDays { get; set; }

    public int StartedTimeOfDay { get; set; }

    public int CompletedTimeOfDay { get; set; }

    public string LocationName { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastUpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public List<string> ParticipantNames { get; set; } = new();

    public List<NarrativeBeat> Beats { get; set; } = new();

    public void Normalize()
    {
        EpisodeId ??= string.Empty;
        EventId ??= string.Empty;
        GameDate ??= string.Empty;
        LocationName ??= string.Empty;
        ParticipantNames = (ParticipantNames ?? new List<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Beats = (Beats ?? new List<NarrativeBeat>())
            .Where(beat => beat is not null)
            .ToList();
        foreach (NarrativeBeat beat in Beats)
            beat.Normalize();
    }
}

/// <summary>One visible line, confirmed choice, or completed gift inside a vanilla event.</summary>
public sealed class NarrativeBeat
{
    public int Sequence { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>The NPC to whose continuity this beat belongs, even when the player is the speaker.</summary>
    public string NpcName { get; set; } = string.Empty;

    public string SpeakerName { get; set; } = string.Empty;

    public string SpeakerDisplayName { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string TranslationKey { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int? GiftTaste { get; set; }

    public string DedupeKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public void Normalize()
    {
        Kind ??= string.Empty;
        NpcName ??= string.Empty;
        SpeakerName ??= string.Empty;
        SpeakerDisplayName ??= string.Empty;
        Text ??= string.Empty;
        TranslationKey ??= string.Empty;
        ItemId ??= string.Empty;
        ItemName ??= string.Empty;
        DedupeKey ??= string.Empty;
        Quantity = Math.Max(0, Quantity);
    }
}
