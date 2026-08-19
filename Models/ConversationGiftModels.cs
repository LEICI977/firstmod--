namespace VivantValley;

/// <summary>The authoritative result of a planned conversation gift action.</summary>
public enum ConversationGiftOutcome
{
    None,
    ImmediateDelivered,
    MailScheduled,
    Rejected,
    Failed,
}

/// <summary>
/// A game-side gift result produced before the player-visible NPC reply is generated.
/// Only committed outcomes may be described as delivered by the model.
/// </summary>
public sealed class ConversationGiftExecutionResult
{
    public string RequestedToolName { get; init; } = NpcGiftToolNames.None;

    public ConversationGiftOutcome Outcome { get; init; }

    public SocialGiftCandidate? Candidate { get; init; }

    public int Quantity { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public bool IsCommitted => Outcome is ConversationGiftOutcome.ImmediateDelivered
        or ConversationGiftOutcome.MailScheduled;

    public bool IsImmediate => Outcome == ConversationGiftOutcome.ImmediateDelivered;

    public bool IsMail => Outcome == ConversationGiftOutcome.MailScheduled;

    public static ConversationGiftExecutionResult NoAction(string requestedToolName = NpcGiftToolNames.None)
        => new()
        {
            RequestedToolName = requestedToolName,
            Outcome = ConversationGiftOutcome.None,
        };
}
