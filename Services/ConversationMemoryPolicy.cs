namespace VivantValley.Services;

/// <summary>Bounds recent dialogue and provides human-like, partial recall of older memories.</summary>
public static class ConversationMemoryPolicy
{
    public const int RecentConversationTurns = 16;
    public const int MaximumLongTermCharacters = 2000;
    public const int MaximumMemoryEntryCharacters = 320;
    public const int MaximumRecallCharacters = 900;
    public const int MaximumRecallEntries = 4;

    public static string UpdateLongTermMemory(
        string? existingSummary,
        string? summaryPatch,
        string? gameDate,
        string? playerId,
        string? npcName,
        long conversationTurn)
    {
        List<string> entries = ParseEntries(existingSummary);
        string patch = LimitSingleLine(summaryPatch, MaximumMemoryEntryCharacters);
        if (patch.Length > 0)
        {
            string date = LimitSingleLine(gameDate, 80);
            entries.Add($"[{(date.Length > 0 ? date : "日期不详")}] {patch}");
        }

        Random random = CreateStableRandom(playerId, npcName, gameDate, conversationTurn.ToString());
        while (JoinedLength(entries) > MaximumLongTermCharacters && entries.Count > 1)
        {
            // Keep the memory created by this turn; forgetting applies to older memories.
            entries.RemoveAt(random.Next(entries.Count - 1));
        }

        if (entries.Count == 0)
            return string.Empty;

        string result = string.Join('\n', entries);
        return result.Length <= MaximumLongTermCharacters
            ? result
            : result[..MaximumLongTermCharacters].TrimEnd();
    }

    public static string BuildRandomRecall(
        string? summary,
        string? playerId,
        string? npcName,
        string? gameDate,
        long conversationTurn)
    {
        List<string> entries = ParseEntries(summary);
        if (entries.Count == 0)
            return string.Empty;

        var indices = Enumerable.Range(0, entries.Count).ToList();
        Random random = CreateStableRandom(playerId, npcName, gameDate, conversationTurn.ToString(), "recall");
        for (int index = indices.Count - 1; index > 0; index--)
        {
            int swap = random.Next(index + 1);
            (indices[index], indices[swap]) = (indices[swap], indices[index]);
        }

        var selected = new List<int>();
        int selectedCharacters = 0;
        foreach (int index in indices)
        {
            int additional = entries[index].Length + (selected.Count > 0 ? 1 : 0);
            if (selected.Count >= MaximumRecallEntries || selectedCharacters + additional > MaximumRecallCharacters)
                continue;

            selected.Add(index);
            selectedCharacters += additional;
        }

        if (selected.Count == 0)
        {
            string only = entries[indices[0]];
            return only.Length <= MaximumRecallCharacters
                ? only
                : only[..MaximumRecallCharacters].TrimEnd();
        }

        selected.Sort();
        return string.Join('\n', selected.Select(index => entries[index]));
    }

    public static List<ConversationMemoryMessage> KeepRecentConversationTurns(
        IEnumerable<ConversationMemoryMessage>? messages,
        int maximumTurns = RecentConversationTurns)
    {
        List<ConversationMemoryMessage> source = (messages ?? Array.Empty<ConversationMemoryMessage>())
            .Where(message => message is not null)
            .ToList();
        if (maximumTurns <= 0 || source.Count == 0)
            return new List<ConversationMemoryMessage>();

        int turnCount = 0;
        int start = 0;
        bool foundLimit = false;
        for (int index = source.Count - 1; index >= 0; index--)
        {
            ConversationMemoryMessage message = source[index];
            bool isAiChat = string.IsNullOrWhiteSpace(message.Source)
                            || string.Equals(message.Source, ConversationMemorySources.AiChat, StringComparison.Ordinal);
            if (!isAiChat || !string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            turnCount++;
            if (turnCount == maximumTurns)
            {
                start = index;
                foundLimit = true;
                break;
            }
        }

        // Non-chat event records are bounded too, so a save cannot grow forever before 16 AI turns exist.
        int hardStart = Math.Max(0, source.Count - (maximumTurns * 4));
        start = foundLimit ? Math.Max(start, hardStart) : hardStart;
        return source.Skip(start).ToList();
    }

    private static List<string> ParseEntries(string? summary)
    {
        var result = new List<string>();
        foreach (string raw in (summary ?? string.Empty).Split(
                     new[] { "\r\n", "\n", "\r" },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string entry = LimitSingleLine(raw, MaximumLongTermCharacters);
            if (entry.Length == 0)
                continue;
            if (!entry.StartsWith("[", StringComparison.Ordinal) || !entry.Contains(']', StringComparison.Ordinal))
                entry = "[日期不详] " + entry;
            result.Add(entry);
        }
        return result;
    }

    private static int JoinedLength(IReadOnlyList<string> entries)
        => entries.Sum(entry => entry.Length) + Math.Max(0, entries.Count - 1);

    private static string LimitSingleLine(string? value, int maximumLength)
    {
        string normalized = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength].TrimEnd();
    }

    private static Random CreateStableRandom(params string?[] values)
    {
        uint hash = 2166136261;
        foreach (string? value in values)
        {
            foreach (char character in value ?? string.Empty)
            {
                hash ^= character;
                hash *= 16777619;
            }
            hash ^= 0xff;
            hash *= 16777619;
        }
        return new Random(unchecked((int)hash));
    }
}
