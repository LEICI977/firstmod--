namespace VivantValley;

public static class NpcMoveToolNames
{
    public const string MoveTo = "move_to";
}

/// <summary>A game-owned destination which may be offered to the model for one conversation turn.</summary>
public sealed class ConversationMoveDestination
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string StartLocationName { get; init; } = string.Empty;

    public string TargetLocationName { get; init; } = string.Empty;

    public int SourceExitTileX { get; init; }

    public int SourceExitTileY { get; init; }

    public int ArrivalTileX { get; init; }

    public int ArrivalTileY { get; init; }

    public int TargetTileX { get; init; }

    public int TargetTileY { get; init; }

    public IReadOnlyList<ConversationMoveRouteStep> RouteSteps { get; init; }
        = Array.Empty<ConversationMoveRouteStep>();
}

/// <summary>One explicit, game-validated map transition in an NPC journey.</summary>
public sealed class ConversationMoveRouteStep
{
    public string SourceLocationName { get; init; } = string.Empty;

    public int SourceExitTileX { get; init; }

    public int SourceExitTileY { get; init; }

    public string TargetLocationName { get; init; } = string.Empty;

    public int ArrivalTileX { get; init; }

    public int ArrivalTileY { get; init; }
}

public enum ConversationMoveOutcome
{
    None,
    Following,
    Traveling,
    Rejected,
    Failed,
}

/// <summary>The authoritative result of asking SMAPI to start one NPC journey.</summary>
public sealed class ConversationMoveExecutionResult
{
    public string RequestedToolName { get; init; } = NpcGiftToolNames.None;

    public ConversationMoveOutcome Outcome { get; init; }

    public ConversationMoveDestination? Destination { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public bool IsCommitted => Outcome is ConversationMoveOutcome.Following or ConversationMoveOutcome.Traveling;

    public static ConversationMoveExecutionResult NoAction()
        => new();
}
