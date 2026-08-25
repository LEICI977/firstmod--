namespace VivantValley;

public static class NpcFishingToolNames
{
    public const string InviteFishingCompanion = "invite_fishing_companion";
}

public enum ConversationFishingOutcome
{
    None,
    Following,
    Rejected,
    Failed,
}

/// <summary>Authoritative result of starting one NPC fishing-companion session.</summary>
public sealed class ConversationFishingExecutionResult
{
    public string RequestedToolName { get; init; } = NpcGiftToolNames.None;

    public ConversationFishingOutcome Outcome { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public bool IsCommitted => Outcome == ConversationFishingOutcome.Following;

    public static ConversationFishingExecutionResult NoAction()
        => new();
}
