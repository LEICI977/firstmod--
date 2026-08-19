using System;
using System.Collections.Generic;

namespace VivantValley;

/// <summary>
/// Persisted state for deterministic NPC actions. This intentionally lives beside,
/// rather than inside, chat memory so forgetting transcripts doesn't corrupt story progress.
/// </summary>
public sealed class NarrativeSaveStore
{
    public int SchemaVersion { get; set; } = 3;

    public Dictionary<string, Dictionary<string, NpcNarrativeState>> Players { get; set; }
        = new(StringComparer.Ordinal);

    public NpcNarrativeState GetOrCreate(string playerId, string npcName)
    {
        playerId = RequireIdentifier(playerId, nameof(playerId));
        npcName = RequireIdentifier(npcName, nameof(npcName));
        Players ??= new Dictionary<string, Dictionary<string, NpcNarrativeState>>(StringComparer.Ordinal);

        if (!Players.TryGetValue(playerId, out Dictionary<string, NpcNarrativeState>? npcStates)
            || npcStates is null)
        {
            npcStates = new Dictionary<string, NpcNarrativeState>(StringComparer.OrdinalIgnoreCase);
            Players[playerId] = npcStates;
        }

        if (!npcStates.TryGetValue(npcName, out NpcNarrativeState? state) || state is null)
        {
            state = new NpcNarrativeState
            {
                PlayerId = playerId,
                NpcName = npcName,
            };
            npcStates[npcName] = state;
        }

        state.PlayerId = playerId;
        state.NpcName = npcName;
        state.RecentUserExcerpt ??= string.Empty;
        state.RecentAssistantExcerpt ??= string.Empty;
        state.CompletedActionIds ??= new HashSet<string>(StringComparer.Ordinal);
        state.CompletedStoryIds ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        state.Flags ??= new HashSet<string>(StringComparer.Ordinal);
        return state;
    }

    public bool TryGet(string playerId, string npcName, out NpcNarrativeState? state)
    {
        state = null;
        return !string.IsNullOrWhiteSpace(playerId)
               && !string.IsNullOrWhiteSpace(npcName)
               && Players is not null
               && Players.TryGetValue(playerId.Trim(), out Dictionary<string, NpcNarrativeState>? npcStates)
               && npcStates is not null
               && npcStates.TryGetValue(npcName.Trim(), out state)
               && state is not null;
    }

    private static string RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("标识不能为空。", parameterName);
        return value.Trim();
    }
}

/// <summary>Relationship and story state for one player/NPC pair.</summary>
public sealed class NpcNarrativeState
{
    public string PlayerId { get; set; } = string.Empty;

    public string NpcName { get; set; } = string.Empty;

    public int Trust { get; set; }

    public int Affection { get; set; }

    public int LastChatDay { get; set; } = -1;

    public int LastEncounterDay { get; set; } = -1000;

    public int LastGiftDay { get; set; } = -1000;

    public long LastConversationTurnScheduled { get; set; }

    public string RecentUserExcerpt { get; set; } = string.Empty;

    public string RecentAssistantExcerpt { get; set; } = string.Empty;

    public PlannedNpcEncounter? PendingEncounter { get; set; }

    /// <summary>Idempotency ledger used to ensure a gift action can't be committed twice.</summary>
    public HashSet<string> CompletedActionIds { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Stable authored story IDs completed by this player/NPC pair.</summary>
    public HashSet<string> CompletedStoryIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Story flags used as prerequisites for later authored nodes.</summary>
    public HashSet<string> Flags { get; set; } = new(StringComparer.Ordinal);
}

public enum PlannedEncounterStatus
{
    Planned,
    Generating,
    Ready,
    Presenting,
    Completed,
    Deferred,
    Expired,
    Cancelled,
}

/// <summary>A resolved authored story action persisted independently from its source JSON.</summary>
public sealed class PlannedNpcEncounter
{
    public string ActionId { get; set; } = Guid.NewGuid().ToString("N");

    public string NpcName { get; set; } = string.Empty;

    public string StoryId { get; set; } = string.Empty;

    public int StoryVersion { get; set; } = 1;

    public bool Repeatable { get; set; }

    public long SourceConversationTurn { get; set; }

    public int EarliestDay { get; set; }

    public int ExpiryDay { get; set; }

    public int StartTime { get; set; } = 900;

    public int EndTime { get; set; } = 2200;

    public float ActivationDistanceTiles { get; set; } = 8f;

    public string GiftItemId { get; set; } = "(O)80";

    public PlannedEncounterStatus Status { get; set; } = PlannedEncounterStatus.Planned;

    public int Attempts { get; set; }

    public string TriggerExcerpt { get; set; } = string.Empty;

    public string AiBrief { get; set; } = string.Empty;

    public string FallbackText { get; set; } = string.Empty;

    public string AcceptText { get; set; } = "收下{GiftDisplayName}";

    public string DeferText { get; set; } = "改天再说";

    public int TrustOnAccept { get; set; } = 2;

    public int AffectionOnAccept { get; set; } = 3;

    public HashSet<string> FlagsOnAccept { get; set; } = new(StringComparer.Ordinal);

    public List<PlannedStoryChoice> Choices { get; set; } = new();
}

/// <summary>A persisted choice contract resolved from the authored story JSON.</summary>
public sealed class PlannedStoryChoice
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string MemoryText { get; set; } = string.Empty;

    public bool ReceiveGift { get; set; }

    public bool Defer { get; set; }

    public string NextStoryId { get; set; } = string.Empty;

    public int Trust { get; set; }

    public int Affection { get; set; }

    public HashSet<string> SetFlags { get; set; } = new(StringComparer.Ordinal);
}
