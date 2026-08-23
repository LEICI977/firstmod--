using System.Globalization;
using StardewModdingAPI;
using StardewValley;

namespace VivantValley.Services;

/// <summary>Offers shared-trip destinations and owns active NPC follower sessions.</summary>
public sealed class NpcMoveToolService
{
    private readonly IMonitor monitor;
    private readonly NpcCombatStateService combatState;
    private readonly ConversationSessionMemoryStore sessionMemory;
    private readonly NpcTilePathfinder pathfinder = new();
    private readonly NpcScheduleRecoveryService scheduleRecovery;
    private readonly Dictionary<string, NpcTravelSession> sessions = new(StringComparer.Ordinal);

    public NpcMoveToolService(
        IMonitor monitor,
        NpcCombatStateService combatState,
        ConversationSessionMemoryStore sessionMemory)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.combatState = combatState ?? throw new ArgumentNullException(nameof(combatState));
        this.sessionMemory = sessionMemory ?? throw new ArgumentNullException(nameof(sessionMemory));
        scheduleRecovery = new NpcScheduleRecoveryService(this.monitor);
    }

    public IReadOnlyList<ConversationMoveDestination> BuildDestinations(NPC npc, GameLocation? playerLocation)
    {
        ArgumentNullException.ThrowIfNull(npc);
        Farmer? leader = Game1.player;
        if (leader is null || !CanStartMovement(npc, leader, playerLocation, out _))
            return Array.Empty<ConversationMoveDestination>();

        string currentLocationName = npc.currentLocation.NameOrUniqueName;
        return Game1.locations
            .Where(location => location is not null)
            .Where(location =>
                !location.NameOrUniqueName.Equals(currentLocationName, StringComparison.OrdinalIgnoreCase)
                && !NpcMineGuardService.IsMineLocation(location)
                && location.currentEvent is null
                && !string.IsNullOrWhiteSpace(location.NameOrUniqueName))
            .GroupBy(location => location.NameOrUniqueName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(location => new ConversationMoveDestination
            {
                Key = "location:" + location.NameOrUniqueName.ToLowerInvariant(),
                DisplayName = CleanDisplayName(location),
                StartLocationName = currentLocationName,
                TargetLocationName = location.NameOrUniqueName,
            })
            .ToArray();
    }

    public ConversationMoveExecutionResult Execute(
        NPC npc,
        Farmer leader,
        ConversationMoveDestination destination)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(leader);
        ArgumentNullException.ThrowIfNull(destination);
        if (!CanStartMovement(npc, leader, leader.currentLocation, out string failureReason))
            return Rejected(destination, failureReason);

        GameLocation? targetLocation = FindLocation(destination.TargetLocationName);
        string expectedKey = "location:" + destination.TargetLocationName.ToLowerInvariant();
        if (!destination.Key.Equals(expectedKey, StringComparison.Ordinal)
            || targetLocation is null
            || NpcMineGuardService.IsMineLocation(targetLocation)
            || targetLocation.currentEvent is not null
            || ReferenceEquals(targetLocation, leader.currentLocation))
        {
            return Rejected(
                destination,
                targetLocation is not null && NpcMineGuardService.IsMineLocation(targetLocation)
                    ? "mine_destination_requires_guard"
                    : "destination_no_longer_available");
        }

        try
        {
            if (sessions.TryGetValue(npc.Name, out NpcTravelSession? existingSession))
            {
                existingSession.Cancel("replaced_by_new_move");
                sessions.Remove(npc.Name);
            }

            npc.controller = null;
            npc.temporaryController = null;
            npc.Halt();
            sessions[npc.Name] = new NpcTravelSession(
                npc,
                leader,
                destination,
                pathfinder,
                monitor,
                scheduleRecovery,
                onDestinationReached: () => sessionMemory.MarkArrived(
                    leader.UniqueMultiplayerID.ToString(CultureInfo.InvariantCulture),
                    npc.Name,
                    destination),
                onSessionEnded: () => sessionMemory.EndMove(
                    leader.UniqueMultiplayerID.ToString(CultureInfo.InvariantCulture),
                    npc.Name,
                    destination));
            sessionMemory.StartMove(
                leader.UniqueMultiplayerID.ToString(CultureInfo.InvariantCulture),
                npc.Name,
                $"{Game1.Date} {Game1.timeOfDay}",
                destination);
            monitor.Log(
                $"move_to following_started npc={npc.Name} leader={leader.UniqueMultiplayerID} "
                + $"destination={destination.Key} source={npc.currentLocation.NameOrUniqueName}.",
                LogLevel.Info);
            return new ConversationMoveExecutionResult
            {
                RequestedToolName = NpcMoveToolNames.MoveTo,
                Outcome = ConversationMoveOutcome.Following,
                Destination = destination,
            };
        }
        catch (Exception exception)
        {
            return new ConversationMoveExecutionResult
            {
                RequestedToolName = NpcMoveToolNames.MoveTo,
                Outcome = ConversationMoveOutcome.Failed,
                Destination = destination,
                FailureReason = CleanReason(exception.Message, "movement_failed"),
            };
        }
    }

    public void SetTravelBarks(string npcName, IEnumerable<string>? barks)
    {
        if (!string.IsNullOrWhiteSpace(npcName)
            && sessions.TryGetValue(npcName, out NpcTravelSession? session))
        {
            session.SetTravelBarks(barks);
        }
    }

    public void Update()
    {
        foreach ((string npcName, NpcTravelSession session) in sessions.ToArray())
        {
            session.Update();
            if (session.IsComplete)
                sessions.Remove(npcName);
        }
    }

    public void CancelAll(string reason)
    {
        foreach (NpcTravelSession session in sessions.Values)
            session.Cancel(reason);
        sessions.Clear();
    }

    private bool CanStartMovement(
        NPC npc,
        Farmer leader,
        GameLocation? playerLocation,
        out string failureReason)
    {
        failureReason = string.Empty;
        if (!Game1.IsMasterGame)
            failureReason = "host_required";
        else if (playerLocation is null
                 || npc.currentLocation is null
                 || !ReferenceEquals(leader.currentLocation, playerLocation)
                 || !ReferenceEquals(npc.currentLocation, playerLocation))
            failureReason = "npc_not_with_player";
        else if (!npc.IsVillager || npc.IsMonster || npc.IsInvisible || !npc.CanSocialize)
            failureReason = "npc_unavailable";
        else if (combatState.IsHospitalized(npc.Name))
            failureReason = "npc_hospitalized";
        else if (Game1.eventUp || Game1.isFestival() || npc.currentLocation.currentEvent is not null)
            failureReason = "event_active";

        return failureReason.Length == 0;
    }

    private static GameLocation? FindLocation(string locationName)
    {
        GameLocation? resolved = Game1.getLocationFromName(locationName);
        return resolved ?? Game1.locations.FirstOrDefault(location =>
            location.NameOrUniqueName.Equals(locationName, StringComparison.OrdinalIgnoreCase)
            || location.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanDisplayName(GameLocation location)
    {
        string displayName = string.IsNullOrWhiteSpace(location.DisplayName)
            ? location.Name
            : location.DisplayName;
        return displayName.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static string CleanReason(string? value, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (clean.Length == 0)
            return fallback;
        return clean.Length <= 160 ? clean : clean[..160];
    }

    private static ConversationMoveExecutionResult Rejected(
        ConversationMoveDestination destination,
        string reason)
        => new()
        {
            RequestedToolName = NpcMoveToolNames.MoveTo,
            Outcome = ConversationMoveOutcome.Rejected,
            Destination = destination,
            FailureReason = reason,
        };
}
