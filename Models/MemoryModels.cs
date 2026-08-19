using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VivantValley;

/// <summary>
/// The complete persisted memory collection. Memories are first partitioned by
/// player and then by NPC so that split-screen/multi-player saves never share a
/// conversation accidentally.
/// </summary>
public sealed class ConversationMemoryStore
{
    public Dictionary<string, Dictionary<string, NpcConversationMemory>> Players { get; set; }
        = new(StringComparer.Ordinal);

    /// <summary>Vanilla story events, kept outside rolling chat so compaction cannot split a scene.</summary>
    public Dictionary<string, List<NarrativeEpisode>> NarrativeEpisodes { get; set; }
        = new(StringComparer.Ordinal);

    public void Normalize()
    {
        Players ??= new Dictionary<string, Dictionary<string, NpcConversationMemory>>(StringComparer.Ordinal);
        foreach ((string playerId, Dictionary<string, NpcConversationMemory>? memories) in Players.ToArray())
        {
            if (memories is null)
            {
                Players[playerId] = new Dictionary<string, NpcConversationMemory>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            foreach ((string npcName, NpcConversationMemory? memory) in memories.ToArray())
            {
                if (memory is null)
                {
                    memories.Remove(npcName);
                    continue;
                }

                memory.PlayerId = string.IsNullOrWhiteSpace(memory.PlayerId) ? playerId : memory.PlayerId;
                memory.NpcName = string.IsNullOrWhiteSpace(memory.NpcName) ? npcName : memory.NpcName;
                memory.Summary ??= string.Empty;
                memory.Messages ??= new List<ConversationMemoryMessage>();
                memory.LastDate ??= string.Empty;
            }
        }

        NarrativeEpisodes ??= new Dictionary<string, List<NarrativeEpisode>>(StringComparer.Ordinal);
        foreach ((string playerId, List<NarrativeEpisode>? episodes) in NarrativeEpisodes.ToArray())
        {
            if (episodes is null)
            {
                NarrativeEpisodes[playerId] = new List<NarrativeEpisode>();
                continue;
            }

            episodes.RemoveAll(episode => episode is null);
            foreach (NarrativeEpisode episode in episodes)
                episode.Normalize();
        }
    }

    public List<NarrativeEpisode> GetNarrativeEpisodes(string playerId)
    {
        playerId = RequireIdentifier(playerId, nameof(playerId));
        NarrativeEpisodes ??= new Dictionary<string, List<NarrativeEpisode>>(StringComparer.Ordinal);
        if (!NarrativeEpisodes.TryGetValue(playerId, out List<NarrativeEpisode>? episodes) || episodes is null)
        {
            episodes = new List<NarrativeEpisode>();
            NarrativeEpisodes[playerId] = episodes;
        }

        return episodes;
    }

    public int ForgetNarrativeEpisodes(string playerId, string? npcName)
    {
        if (string.IsNullOrWhiteSpace(playerId)
            || NarrativeEpisodes is null
            || !NarrativeEpisodes.TryGetValue(playerId.Trim(), out List<NarrativeEpisode>? episodes)
            || episodes is null)
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(npcName))
        {
            int count = episodes.Count;
            episodes.Clear();
            return count;
        }

        return episodes.RemoveAll(episode =>
            episode.ParticipantNames.Any(name => name.Equals(npcName.Trim(), StringComparison.OrdinalIgnoreCase))
            || episode.Beats.Any(beat => beat.NpcName.Equals(npcName.Trim(), StringComparison.OrdinalIgnoreCase)));
    }

    public NpcConversationMemory GetOrCreate(string playerId, string npcName)
    {
        playerId = RequireIdentifier(playerId, nameof(playerId));
        npcName = RequireIdentifier(npcName, nameof(npcName));

        Players ??= new Dictionary<string, Dictionary<string, NpcConversationMemory>>(StringComparer.Ordinal);

        if (!Players.TryGetValue(playerId, out Dictionary<string, NpcConversationMemory>? npcMemories)
            || npcMemories is null)
        {
            npcMemories = new Dictionary<string, NpcConversationMemory>(StringComparer.OrdinalIgnoreCase);
            Players[playerId] = npcMemories;
        }

        if (!npcMemories.TryGetValue(npcName, out NpcConversationMemory? memory) || memory is null)
        {
            memory = new NpcConversationMemory
            {
                PlayerId = playerId,
                NpcName = npcName,
            };
            npcMemories[npcName] = memory;
        }

        // Repair identifiers in older/partially-written save data.
        memory.PlayerId = playerId;
        memory.NpcName = npcName;
        memory.Messages ??= new List<ConversationMemoryMessage>();
        memory.Summary ??= string.Empty;
        memory.LastDate ??= string.Empty;
        return memory;
    }

    public bool TryGet(string playerId, string npcName, out NpcConversationMemory? memory)
    {
        memory = null;
        if (string.IsNullOrWhiteSpace(playerId)
            || string.IsNullOrWhiteSpace(npcName)
            || Players is null
            || !Players.TryGetValue(playerId.Trim(), out Dictionary<string, NpcConversationMemory>? npcMemories)
            || npcMemories is null)
        {
            return false;
        }

        return npcMemories.TryGetValue(npcName.Trim(), out memory) && memory is not null;
    }

    private static string RequireIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("标识不能为空。", parameterName);

        return value.Trim();
    }
}

/// <summary>A snapshot of one player's memory with one NPC.</summary>
public sealed class NpcConversationMemory
{
    public string PlayerId { get; set; } = string.Empty;

    public string NpcName { get; set; } = string.Empty;

    /// <summary>Compressed long-term memory covering messages removed from <see cref="Messages"/>.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Uncompressed recent messages, in chronological order.</summary>
    public List<ConversationMemoryMessage> Messages { get; set; } = new();

    /// <summary>Number of completed player/NPC dialogue turns.</summary>
    public long TotalTurns { get; set; }

    /// <summary>The in-game date supplied by the caller for the most recent completed turn.</summary>
    public string LastDate { get; set; } = string.Empty;

    public NpcConversationMemory Clone()
    {
        var clone = new NpcConversationMemory
        {
            PlayerId = PlayerId ?? string.Empty,
            NpcName = NpcName ?? string.Empty,
            Summary = Summary ?? string.Empty,
            TotalTurns = TotalTurns,
            LastDate = LastDate ?? string.Empty,
        };

        if (Messages is not null)
        {
            foreach (ConversationMemoryMessage? message in Messages)
            {
                if (message is not null)
                    clone.Messages.Add(message.Clone());
            }
        }

        return clone;
    }
}

public sealed class ConversationMemoryMessage
{
    /// <summary>Normally <c>user</c> or <c>assistant</c>.</summary>
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>Optional in-game date associated with this message.</summary>
    public string GameDate { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional origin such as ai_chat, vanilla_dialogue, vanilla_choice, or vanilla_gift.</summary>
    public string Source { get; set; } = string.Empty;

    public string EventId { get; set; } = string.Empty;

    public string EpisodeId { get; set; } = string.Empty;

    public string LocationName { get; set; } = string.Empty;

    public string TranslationKey { get; set; } = string.Empty;

    public string DedupeKey { get; set; } = string.Empty;

    public ConversationMemoryMessage Clone()
    {
        return new ConversationMemoryMessage
        {
            Role = Role ?? string.Empty,
            Content = Content ?? string.Empty,
            GameDate = GameDate ?? string.Empty,
            CreatedAtUtc = CreatedAtUtc,
            Source = Source ?? string.Empty,
            EventId = EventId ?? string.Empty,
            EpisodeId = EpisodeId ?? string.Empty,
            LocationName = LocationName ?? string.Empty,
            TranslationKey = TranslationKey ?? string.Empty,
            DedupeKey = DedupeKey ?? string.Empty,
        };
    }
}

/// <summary>Exact message shape accepted by DeepSeek's chat/completions endpoint.</summary>
public sealed class DeepSeekChatMessage
{
    public DeepSeekChatMessage()
    {
    }

    public DeepSeekChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>One incremental server-sent event from a streaming DeepSeek response.</summary>
public sealed record DeepSeekStreamChunk(string ContentDelta, string ReasoningDelta);

public sealed class DeepSeekThinkingOptions
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "disabled";
}

/// <summary>
/// Request shape intentionally limited to the fields used by the documented
/// DeepSeek example, including the stream flag used by the mod's SSE client.
/// </summary>
public sealed class DeepSeekChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "deepseek-v4-flash";

    [JsonPropertyName("messages")]
    public List<DeepSeekChatMessage> Messages { get; set; } = new();

    [JsonPropertyName("thinking")]
    public DeepSeekThinkingOptions Thinking { get; set; } = new();

    [JsonPropertyName("reasoning_effort")]
    public string ReasoningEffort { get; set; } = "low";

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 4096;

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}

public sealed class ConversationEngineOptions
{
    public string Model { get; set; } = "deepseek-v4-flash";

    public string ThinkingType { get; set; } = "disabled";

    public string ReasoningEffort { get; set; } = "low";

    /// <summary>Maximum number of recent uncompressed messages sent with a chat request.</summary>
    public int MaxContextMessages { get; set; } = 32;

    /// <summary>Maximum output tokens requested for either summarization or a normal reply.</summary>
    public int MaxOutputTokens { get; set; } = 4096;

    /// <summary>
    /// A summary is attempted when adding the next user/assistant pair would
    /// make the uncompressed message count exceed this value.
    /// </summary>
    public int SummaryTriggerMessageCount { get; set; } = 24;

    /// <summary>Number of newest messages kept verbatim after a successful summary.</summary>
    public int RecentMessagesToKeep { get; set; } = 10;
}

public sealed class MemoryCompactionInfo
{
    public bool ThresholdExceeded { get; set; }

    public bool SummaryAttempted { get; set; }

    public bool SummarySucceeded { get; set; }

    /// <summary>True when summarization failed but the normal chat request still ran.</summary>
    public bool ContinuedAfterSummaryFailure { get; set; }

    public int PrunedMessageCount { get; set; }

    public int KeptMessageCount { get; set; }

    public string PreviousSummary { get; set; } = string.Empty;

    public string UpdatedSummary { get; set; } = string.Empty;

    /// <summary>A sanitized diagnostic. It never intentionally contains the API key.</summary>
    public string SummaryFailureReason { get; set; } = string.Empty;
}

public sealed class ConversationEngineResult
{
    public string Reply { get; set; } = string.Empty;

    /// <summary>A new snapshot; the input snapshot is never mutated.</summary>
    public NpcConversationMemory UpdatedMemory { get; set; } = new();

    public MemoryCompactionInfo Compaction { get; set; } = new();
}
