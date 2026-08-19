using System.Text;

namespace VivantValley.Services;

/// <summary>Builds bounded prompt context without mutating or compacting the persisted episode archive.</summary>
public sealed class NarrativeContextService
{
    public string Build(
        IEnumerable<NarrativeEpisode>? episodes,
        string npcName,
        int maximumCompleteEpisodes,
        int maximumEpisodeAnchors,
        int preferredMaximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(npcName))
            return string.Empty;

        maximumCompleteEpisodes = Math.Clamp(maximumCompleteEpisodes, 1, 20);
        maximumEpisodeAnchors = Math.Clamp(maximumEpisodeAnchors, 0, 100);
        preferredMaximumCharacters = Math.Clamp(preferredMaximumCharacters, 2000, 50000);

        NarrativeEpisode[] relevant = (episodes ?? Array.Empty<NarrativeEpisode>())
            .Where(episode => episode is not null && IsRelevant(episode, npcName))
            .OrderBy(episode => episode.TotalDays)
            .ThenBy(episode => episode.StartedTimeOfDay)
            .ThenBy(episode => episode.StartedAtUtc)
            .ToArray();
        if (relevant.Length == 0)
            return string.Empty;

        NarrativeEpisode[] complete = relevant.TakeLast(maximumCompleteEpisodes).ToArray();
        HashSet<string> completeIds = complete
            .Select(episode => episode.EpisodeId)
            .ToHashSet(StringComparer.Ordinal);
        NarrativeEpisode[] anchored = relevant
            .Where(episode => !completeIds.Contains(episode.EpisodeId))
            .TakeLast(maximumEpisodeAnchors)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("【玩家与该村民已经亲历的原版剧情】");
        builder.AppendLine("以下内容来自玩家实际看见、选择或完成的游戏互动；它们不能覆盖上面的实时存档事实。");

        if (anchored.Length > 0)
        {
            builder.AppendLine("较早剧情的固定节点：");
            foreach (NarrativeEpisode episode in anchored)
            {
                string anchor = RenderAnchor(episode, npcName);
                if (anchor.Length == 0)
                    continue;
                if (builder.Length + anchor.Length + 3 > preferredMaximumCharacters / 2)
                    break;
                builder.Append("- ").AppendLine(anchor);
            }
        }

        builder.AppendLine("最近剧情的实际顺序：");
        for (int index = 0; index < complete.Length; index++)
        {
            NarrativeEpisode episode = complete[index];
            string rendered = RenderCompleteEpisode(episode, npcName);
            if (rendered.Length == 0)
                continue;

            bool isLatest = index == complete.Length - 1;
            if (!isLatest && builder.Length + rendered.Length > preferredMaximumCharacters)
                continue;

            // The newest episode is always kept whole. Older material yields first when context is tight.
            builder.Append(rendered);
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsRelevant(NarrativeEpisode episode, string npcName)
        => episode.ParticipantNames.Any(name => name.Equals(npcName, StringComparison.OrdinalIgnoreCase))
           || episode.Beats.Any(beat => beat.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase));

    private static string RenderAnchor(NarrativeEpisode episode, string npcName)
    {
        NarrativeBeat[] relevant = GetRelevantBeats(episode, npcName);
        if (relevant.Length == 0)
            return string.Empty;

        var facts = new List<string>();
        facts.AddRange(relevant
            .Where(beat => beat.Kind.Equals(NarrativeBeatKinds.PlayerChoice, StringComparison.Ordinal))
            .Select(beat => "玩家选择“" + OneLine(beat.Text) + "”"));
        facts.AddRange(relevant
            .Where(beat => beat.Kind.Equals(NarrativeBeatKinds.Gift, StringComparison.Ordinal))
            .Select(RenderGift));

        NarrativeBeat? ending = relevant.LastOrDefault(beat =>
            beat.Kind.Equals(NarrativeBeatKinds.NpcDialogue, StringComparison.Ordinal)
            || beat.Kind.Equals(NarrativeBeatKinds.Question, StringComparison.Ordinal));
        if (ending is not null)
            facts.Add("最后记得“" + Limit(OneLine(ending.Text), 240) + "”");

        string eventLabel = string.IsNullOrWhiteSpace(episode.EventId) ? "原版事件" : "事件 " + episode.EventId;
        string status = episode.IsCompleted ? "已完成" : "仍在进行";
        string detail = facts.Count == 0 ? "没有额外选择" : string.Join("；", facts.Distinct(StringComparer.Ordinal));
        return $"[{episode.GameDate}] {eventLabel}（{episode.LocationName}，{status}）：{detail}";
    }

    private static string RenderCompleteEpisode(NarrativeEpisode episode, string npcName)
    {
        NarrativeBeat[] relevant = GetRelevantBeats(episode, npcName);
        if (relevant.Length == 0)
            return string.Empty;

        string status = episode.IsCompleted ? "已完成" : "进行中";
        string eventLabel = string.IsNullOrWhiteSpace(episode.EventId) ? "原版事件" : "事件 " + episode.EventId;
        var builder = new StringBuilder();
        builder.Append("[剧情：").Append(eventLabel)
            .Append("；").Append(episode.GameDate)
            .Append("；").Append(episode.LocationName)
            .Append("；").Append(status).AppendLine("]");

        foreach (NarrativeBeat beat in relevant)
        {
            string line = beat.Kind switch
            {
                NarrativeBeatKinds.NpcDialogue =>
                    $"{Fallback(beat.SpeakerDisplayName, beat.SpeakerName, npcName)}：{OneLine(beat.Text)}",
                NarrativeBeatKinds.Question =>
                    $"{Fallback(beat.SpeakerDisplayName, beat.SpeakerName, npcName)}问：{OneLine(beat.Text)}",
                NarrativeBeatKinds.PlayerChoice => "玩家选择：" + OneLine(beat.Text),
                NarrativeBeatKinds.Gift => RenderGift(beat),
                _ => OneLine(beat.Text),
            };
            if (line.Length > 0)
                builder.Append("- ").AppendLine(line);
        }

        return builder.ToString();
    }

    private static NarrativeBeat[] GetRelevantBeats(NarrativeEpisode episode, string npcName)
        => episode.Beats
            .Where(beat => beat.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase)
                           || beat.SpeakerName.Equals(npcName, StringComparison.OrdinalIgnoreCase)
                           || (string.IsNullOrWhiteSpace(beat.NpcName)
                               && (beat.Kind.Equals(NarrativeBeatKinds.Question, StringComparison.Ordinal)
                                   || beat.Kind.Equals(NarrativeBeatKinds.PlayerChoice, StringComparison.Ordinal))))
            .OrderBy(beat => beat.Sequence)
            .ToArray();

    private static string RenderGift(NarrativeBeat beat)
    {
        string quantity = beat.Quantity > 1 ? $" x{beat.Quantity}" : string.Empty;
        string taste = beat.GiftTaste.HasValue ? "，反应=" + DescribeGiftTaste(beat.GiftTaste.Value) : string.Empty;
        return $"玩家送给 {Fallback(beat.SpeakerDisplayName, beat.NpcName, "该村民")} {Fallback(beat.ItemName, beat.ItemId, "礼物")}{quantity}{taste}";
    }

    public static string DescribeGiftTaste(int taste)
        => taste switch
        {
            0 => "最爱",
            2 => "喜欢",
            4 => "不喜欢",
            6 => "讨厌",
            7 => "星之果茶",
            _ => "一般",
        };

    private static string Fallback(string? first, string? second, string fallback)
        => !string.IsNullOrWhiteSpace(first)
            ? first.Trim()
            : !string.IsNullOrWhiteSpace(second)
                ? second.Trim()
                : fallback;

    private static string OneLine(string? text)
        => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Limit(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum] + "...";
}
