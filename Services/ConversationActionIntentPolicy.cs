using System.Text.RegularExpressions;

namespace VivantValley.Services;

/// <summary>Blocks obvious player commands before optional NPC action tools reach the model.</summary>
public static class ConversationActionIntentPolicy
{
    private static readonly Regex DirectEnglishGiftRequest = new(
        @"\b(?:give|send|bring|buy)\s+(?:me|us)\b|\bgift\s+me\b|\bcan\s+i\s+have\b|\bcould\s+i\s+have\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EnglishGiftDemand = new(
        @"\bi\s+(?:want|need)\s+(?:a\s+|some\s+)?(?:gift|present|item|something)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EnglishItemRequest = new(
        @"\b(?:i\s+(?:want|need)|can\s+i\s+have|could\s+i\s+have|give\s+me|bring\s+me)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex EnglishMoveInvitation = new(
        @"\b(?:with\s+me|together|join\s+me|come\s+with|would\s+you|do\s+you\s+want\s+to|let['’]?s)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DirectEnglishMoveCommand = new(
        @"^\s*(?:please\s+)?go\b|\byou\s+(?:should|must|need\s+to|have\s+to)\s+go\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ChineseGiftRequests =
    {
        "给我", "给咱", "给俺", "送我", "送给我", "送咱", "我要礼物", "我想要礼物",
        "给点礼物", "给个礼物", "有没有礼物", "拿礼物来", "来个礼物",
    };

    private static readonly string[] ChineseMoveInvitations =
    {
        "一起去", "一块去", "陪我去", "跟我去", "和我去", "随我去", "要不要去",
        "想不想去", "愿不愿意去", "愿意和我去", "我们去", "咱们去", "带你去",
        "邀请你去", "一起走", "走吧", "出发吧",
    };

    private static readonly string[] ChineseMoveCommands =
    {
        "给我去", "你去", "赶紧去", "马上去", "现在去", "现在就去", "必须去", "应该去",
        "快去", "立刻去",
    };

    public static bool IsDirectGiftRequest(
        string? playerText,
        IEnumerable<string>? candidateDisplayNames = null)
    {
        string text = Normalize(playerText);
        if (text.Length == 0)
            return false;
        if (ChineseGiftRequests.Any(text.Contains))
            return true;

        if (DirectEnglishGiftRequest.IsMatch(playerText ?? string.Empty)
            || EnglishGiftDemand.IsMatch(playerText ?? string.Empty))
        {
            return true;
        }

        bool asksForSpecificItem = text.Contains("我要", StringComparison.Ordinal)
                                   || text.Contains("我想要", StringComparison.Ordinal)
                                   || text.Contains("我需要", StringComparison.Ordinal)
                                   || text.Contains("能不能给", StringComparison.Ordinal)
                                   || text.Contains("可以给", StringComparison.Ordinal)
                                   || text.Contains("有没有", StringComparison.Ordinal)
                                   || text.Contains("来一个", StringComparison.Ordinal)
                                   || text.Contains("来点", StringComparison.Ordinal)
                                   || EnglishItemRequest.IsMatch(playerText ?? string.Empty);
        return asksForSpecificItem
               && (candidateDisplayNames ?? Array.Empty<string>())
               .Any(candidate =>
               {
                   string normalizedCandidate = Normalize(candidate);
                   return normalizedCandidate.Length > 0
                          && text.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase);
               });
    }

    public static bool IsDirectMoveCommand(string? playerText)
    {
        string text = Normalize(playerText);
        if (text.Length == 0)
            return false;

        if (ChineseMoveInvitations.Any(text.Contains)
            || EnglishMoveInvitation.IsMatch(playerText ?? string.Empty))
        {
            return false;
        }

        if (text.Contains("去过", StringComparison.Ordinal)
            || text.Contains("去了", StringComparison.Ordinal)
            || text.Contains("去哪", StringComparison.Ordinal))
        {
            return false;
        }

        if (ChineseMoveCommands.Any(text.Contains))
            return true;
        if (text.StartsWith("去", StringComparison.Ordinal)
            && !text.Contains('吗')
            && !text.Contains('嘛')
            && !text.Contains('?')
            && !text.Contains('？'))
        {
            return true;
        }

        return DirectEnglishMoveCommand.IsMatch(playerText ?? string.Empty);
    }

    private static string Normalize(string? value)
        => string.Concat((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character)))
            .ToLowerInvariant();

}
