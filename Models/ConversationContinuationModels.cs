namespace VivantValley;

public sealed record ConversationContinuationTarget(
    string PlayerId,
    string NpcName,
    string NpcDisplayName,
    int TotalDays);

public enum ConversationContinuationBlockReason
{
    None,
    InvalidTarget,
    PlayerChanged,
    DayChanged,
    NpcUnavailable,
}

public static class ConversationContinuationPolicy
{
    public static ConversationContinuationBlockReason Evaluate(
        ConversationContinuationTarget? target,
        string currentPlayerId,
        int currentDay,
        bool npcAvailable)
    {
        if (target is null
            || string.IsNullOrWhiteSpace(target.PlayerId)
            || string.IsNullOrWhiteSpace(target.NpcName)
            || target.TotalDays < 0)
        {
            return ConversationContinuationBlockReason.InvalidTarget;
        }
        if (!target.PlayerId.Equals(currentPlayerId, StringComparison.Ordinal))
            return ConversationContinuationBlockReason.PlayerChanged;
        if (target.TotalDays != currentDay)
            return ConversationContinuationBlockReason.DayChanged;
        if (!npcAvailable)
            return ConversationContinuationBlockReason.NpcUnavailable;

        return ConversationContinuationBlockReason.None;
    }
}
