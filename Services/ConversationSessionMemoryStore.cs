namespace VivantValley.Services;

/// <summary>
/// Holds short-lived, game-confirmed shared experiences for one player/NPC pair.
/// These facts are intentionally separate from persisted long-term conversation memory.
/// </summary>
public sealed class ConversationSessionMemoryStore
{
    public const int MaximumRetainedConversationTurns = 8;
    public const int MaximumFactsPerNpc = 4;

    private const string TravelingStatus = "traveling";
    private const string ArrivedStatus = "arrived";

    private readonly Dictionary<string, SessionBucket> buckets =
        new(StringComparer.Ordinal);

    public void Clear()
        => buckets.Clear();

    public void StartMove(
        string playerId,
        string npcName,
        string gameDate,
        ConversationMoveDestination destination)
    {
        if (string.IsNullOrWhiteSpace(playerId)
            || string.IsNullOrWhiteSpace(npcName)
            || destination is null
            || string.IsNullOrWhiteSpace(destination.Key)
            || string.IsNullOrWhiteSpace(destination.DisplayName))
        {
            return;
        }

        SessionBucket bucket = GetOrCreateBucket(playerId, npcName);
        bucket.Facts.RemoveAll(fact =>
            fact.DestinationKey.Equals(destination.Key, StringComparison.OrdinalIgnoreCase)
            || fact.Status.Equals(TravelingStatus, StringComparison.Ordinal));
        bucket.Facts.Add(new SessionFact
        {
            DestinationKey = Clean(destination.Key, 128),
            DestinationDisplayName = Clean(destination.DisplayName, 120),
            SourceLocationName = Clean(destination.StartLocationName, 120),
            GameDate = Clean(gameDate, 80),
            Status = TravelingStatus,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        Trim(bucket);
    }

    public void MarkArrived(
        string playerId,
        string npcName,
        ConversationMoveDestination destination)
    {
        if (!TryGetBucket(playerId, npcName, out SessionBucket bucket)
            || destination is null)
        {
            return;
        }

        SessionFact? fact = bucket.Facts
            .Where(candidate => candidate.Status.Equals(TravelingStatus, StringComparison.Ordinal)
                                && candidate.DestinationKey.Equals(
                                    destination.Key,
                                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.UpdatedAtUtc)
            .FirstOrDefault();
        if (fact is null)
            return;

        fact.Status = ArrivedStatus;
        fact.UpdatedAtUtc = DateTimeOffset.UtcNow;
        // The first conversation after arrival starts the eight-turn retention window.
        fact.ArrivalObservedTurn = null;
    }

    public void EndMove(
        string playerId,
        string npcName,
        ConversationMoveDestination destination)
    {
        if (!TryGetBucket(playerId, npcName, out SessionBucket bucket)
            || destination is null)
        {
            return;
        }

        // Keep a completed destination fact, but remove a journey that failed or was
        // cancelled before the NPC arrived there.
        bucket.Facts.RemoveAll(fact =>
            fact.Status.Equals(TravelingStatus, StringComparison.Ordinal)
            && fact.DestinationKey.Equals(destination.Key, StringComparison.OrdinalIgnoreCase));
        if (bucket.Facts.Count == 0)
            buckets.Remove(MakeKey(playerId, npcName));
    }

    public IReadOnlyList<string> BuildPromptFacts(
        string playerId,
        string npcName,
        long currentConversationTurn)
    {
        if (!TryGetBucket(playerId, npcName, out SessionBucket bucket))
            return Array.Empty<string>();

        long boundedTurn = Math.Max(0, currentConversationTurn);
        foreach (SessionFact fact in bucket.Facts.Where(fact =>
                     fact.Status.Equals(ArrivedStatus, StringComparison.Ordinal)
                     && !fact.ArrivalObservedTurn.HasValue).ToArray())
        {
            fact.ArrivalObservedTurn = boundedTurn;
        }

        bucket.Facts.RemoveAll(fact =>
            fact.Status.Equals(ArrivedStatus, StringComparison.Ordinal)
            && fact.ArrivalObservedTurn.HasValue
            && boundedTurn >= fact.ArrivalObservedTurn.Value + MaximumRetainedConversationTurns);
        if (bucket.Facts.Count == 0)
        {
            buckets.Remove(MakeKey(playerId, npcName));
            return Array.Empty<string>();
        }

        return bucket.Facts
            .OrderByDescending(fact => fact.UpdatedAtUtc)
            .Take(MaximumFactsPerNpc)
            .Select(FormatFact)
            .ToArray();
    }

    private SessionBucket GetOrCreateBucket(string playerId, string npcName)
    {
        string key = MakeKey(playerId, npcName);
        if (!buckets.TryGetValue(key, out SessionBucket? bucket))
        {
            bucket = new SessionBucket();
            buckets[key] = bucket;
        }

        return bucket;
    }

    private bool TryGetBucket(
        string playerId,
        string npcName,
        out SessionBucket bucket)
    {
        bucket = null!;
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(npcName))
            return false;

        if (!buckets.TryGetValue(MakeKey(playerId, npcName), out SessionBucket? found)
            || found is null)
        {
            return false;
        }

        bucket = found;
        return true;
    }

    private static void Trim(SessionBucket bucket)
    {
        if (bucket.Facts.Count <= MaximumFactsPerNpc)
            return;

        bucket.Facts = bucket.Facts
            .OrderByDescending(fact => fact.UpdatedAtUtc)
            .Take(MaximumFactsPerNpc)
            .ToList();
    }

    private static string FormatFact(SessionFact fact)
    {
        string date = string.IsNullOrWhiteSpace(fact.GameDate) ? "日期不详" : fact.GameDate;
        string source = string.IsNullOrWhiteSpace(fact.SourceLocationName)
            ? string.Empty
            : $"（从 {fact.SourceLocationName} 出发）";
        string wording = fact.Status.Equals(TravelingStatus, StringComparison.Ordinal)
            ? $"NPC 正在和玩家一起前往 {fact.DestinationDisplayName}{source}"
            : $"NPC 今天已经和玩家一起到达过 {fact.DestinationDisplayName}{source}";
        return $"[{date}] 临时共同经历：{wording}。";
    }

    private static string MakeKey(string playerId, string npcName)
        => $"{playerId.Trim()}\u001f{npcName.Trim()}";

    private static string Clean(string? value, int maximumLength)
    {
        string normalized = string.Join(
            " ",
            (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd();
    }

    private sealed class SessionBucket
    {
        public List<SessionFact> Facts { get; set; } = new();
    }

    private sealed class SessionFact
    {
        public string DestinationKey { get; set; } = string.Empty;
        public string DestinationDisplayName { get; set; } = string.Empty;
        public string SourceLocationName { get; set; } = string.Empty;
        public string GameDate { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public long? ArrivalObservedTurn { get; set; }
    }
}
