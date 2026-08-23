namespace VivantValley;

public static class NpcMineGuardToolNames
{
    public const string InviteMineGuard = "invite_mine_guard";
}

public enum ConversationMineGuardOutcome
{
    None,
    Guarding,
    Rejected,
    Failed,
}

/// <summary>Authoritative result of starting one NPC mine guard session.</summary>
public sealed class ConversationMineGuardExecutionResult
{
    public string RequestedToolName { get; init; } = NpcGiftToolNames.None;

    public ConversationMineGuardOutcome Outcome { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public bool IsCommitted => Outcome == ConversationMineGuardOutcome.Guarding;

    public static ConversationMineGuardExecutionResult NoAction()
        => new();
}
