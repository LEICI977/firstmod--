using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace VivantValley.Services;

/// <summary>Keeps one NPC on a stable trail behind a player-led trip.</summary>
internal sealed class NpcTravelSession
{
    private enum TravelStage
    {
        EnRoute,
        AtDestination,
        Released,
        Failed,
    }

    private const int MinimumFollowDistance = 1;
    private const int MaximumFollowDistance = 1;
    private const int FollowStartDistance = 2;
    private const int ArrivalDistance = 1;
    private const int ArrivalHoldTicks = 30;
    private const int DestinationExitGraceTicks = 30;
    private const int TrailLagTiles = 1;
    private const int TrailWaypointSpan = 4;
    private const int PathRetryTicks = 30;
    private const int CatchUpDistance = 8;
    private const int WaitWarningDistance = 12;
    private const int WaitWarningResetDistance = 8;
    private const int WaitWarningCooldownTicks = 600;
    // Let the player clear a doorway, warp tile, or mine entrance before the NPC follows.
    private const int CrossMapTransferDelayTicks = 90;
    private const int InitialBarkDelayTicks = 30;
    private const int BarkCooldownTicks = 900;

    private readonly NPC npc;
    private readonly Farmer leader;
    private readonly ConversationMoveDestination destination;
    private readonly NpcTilePathfinder pathfinder;
    private readonly IMonitor monitor;
    private readonly NpcScheduleRecoveryService scheduleRecovery;
    private readonly Action onDestinationReached;
    private readonly Action onSessionEnded;
    private readonly NpcNavigationController navigation = new();
    private readonly Queue<string> travelBarks = new();
    private readonly List<Point> leaderTrail = new();
    private readonly int originalSpeed;
    private readonly bool originalIgnoreScheduleToday;
    private bool navigating;
    private bool activeWaypointUsesTrail;
    private bool distanceWarningArmed = true;
    private TravelStage stage = TravelStage.EnRoute;
    private int activeTrailPoints;
    private int pathRetryTicks;
    private int differentLocationTicks;
    private int arrivalTicks;
    private int destinationExitTicks;
    private int barkCooldownTicks = InitialBarkDelayTicks;
    private int distanceWarningCooldownTicks;
    private Point lastLeaderTile;

    public NpcTravelSession(
        NPC npc,
        Farmer leader,
        ConversationMoveDestination destination,
        NpcTilePathfinder pathfinder,
        IMonitor monitor,
        NpcScheduleRecoveryService scheduleRecovery,
        Action onDestinationReached,
        Action onSessionEnded)
    {
        this.npc = npc ?? throw new ArgumentNullException(nameof(npc));
        this.leader = leader ?? throw new ArgumentNullException(nameof(leader));
        this.destination = destination ?? throw new ArgumentNullException(nameof(destination));
        this.pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.scheduleRecovery = scheduleRecovery ?? throw new ArgumentNullException(nameof(scheduleRecovery));
        this.onDestinationReached = onDestinationReached ?? throw new ArgumentNullException(nameof(onDestinationReached));
        this.onSessionEnded = onSessionEnded ?? throw new ArgumentNullException(nameof(onSessionEnded));
        if (!ReferenceEquals(npc.currentLocation, leader.currentLocation))
            throw new ArgumentException("NPC and leader must start in the same location.", nameof(leader));
        originalSpeed = npc.speed;
        originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        npc.ignoreScheduleToday = true;
        ResetLeaderTrail();
        monitor.Log(
            $"move_to following_en_route npc={npc.Name} destination={destination.Key} "
            + $"distance={MaximumFollowDistance}.",
            LogLevel.Debug);
    }

    public bool IsComplete => stage is TravelStage.Released or TravelStage.Failed;

    public string FailureReason { get; private set; } = string.Empty;

    public void SetTravelBarks(IEnumerable<string>? barks)
    {
        travelBarks.Clear();
        foreach (string value in barks ?? Array.Empty<string>())
        {
            string clean = CleanBark(value);
            if (clean.Length > 0 && !travelBarks.Contains(clean))
                travelBarks.Enqueue(clean);
            if (travelBarks.Count >= 3)
                break;
        }
        barkCooldownTicks = InitialBarkDelayTicks;
    }

    public void Update()
    {
        if (IsComplete)
            return;
        if (!Context.IsWorldReady || !Game1.IsMasterGame)
        {
            Fail("world_unavailable");
            return;
        }
        if (leader.currentLocation is null || npc.currentLocation is null)
        {
            Fail("leader_unavailable");
            return;
        }
        npc.ignoreScheduleToday = true;
        if (Game1.eventUp
            || Game1.isFestival()
            || leader.currentLocation.currentEvent is not null
            || npc.currentLocation.currentEvent is not null)
        {
            Fail("event_started");
            return;
        }

        // Single-player pauses beneath menus, so follower timers should pause too.
        if (Game1.activeClickableMenu is not null && !Game1.IsMultiplayer)
            return;

        try
        {
            if (stage == TravelStage.AtDestination && !IsDestinationLocation(leader.currentLocation))
            {
                UpdateDestinationExit();
                return;
            }
            if (stage == TravelStage.AtDestination)
                destinationExitTicks = 0;

            if (!ReferenceEquals(npc.currentLocation, leader.currentLocation))
            {
                WaitForLeaderTransition();
                return;
            }

            differentLocationTicks = 0;
            RecordLeaderTrail();
            double separation = TileDistance(npc.TilePoint, leader.TilePoint);
            UpdateLocalFollow(separation);
            bool warnedPlayer = UpdateDistanceWarning(separation);
            if (!warnedPlayer)
                UpdateTravelBark();
            UpdateArrival(separation);
        }
        catch (Exception exception)
        {
            Fail(CleanReason(exception.Message, "movement_failed"));
        }
    }

    public void Cancel(string reason)
    {
        if (IsComplete)
            return;
        ReleaseNpc();
        FailureReason = CleanReason(reason, "cancelled");
        stage = TravelStage.Released;
        onSessionEnded();
        monitor.Log(
            $"move_to cancelled npc={npc.Name} destination={destination.Key} reason={FailureReason}.",
            LogLevel.Debug);
    }

    private void WaitForLeaderTransition()
    {
        StopNavigation();
        arrivalTicks = 0;
        differentLocationTicks++;
        if (differentLocationTicks < CrossMapTransferDelayTicks)
            return;

        GameLocation target = leader.currentLocation;
        if (target.currentEvent is not null
            || !pathfinder.TryFindSafeFollowTile(
                target,
                npc,
                leader.TilePoint,
                MinimumFollowDistance,
                MaximumFollowDistance + 1,
                out Point arrivalTile))
        {
            return;
        }

        GameLocation source = npc.currentLocation;
        Game1.warpCharacter(npc, target, new Vector2(arrivalTile.X, arrivalTile.Y));
        differentLocationTicks = 0;
        pathRetryTicks = 0;
        distanceWarningArmed = true;
        ResetLeaderTrail();
        monitor.Log(
            $"move_to follower_transferred npc={npc.Name} source={source.NameOrUniqueName} "
            + $"target={target.NameOrUniqueName} tile={arrivalTile.X},{arrivalTile.Y}.",
            LogLevel.Debug);
    }

    private void RecordLeaderTrail()
    {
        Point currentTile = leader.TilePoint;
        if (currentTile == lastLeaderTile)
            return;

        lastLeaderTile = currentTile;
        if (leaderTrail.Count == 0 || leaderTrail[^1] != currentTile)
            leaderTrail.Add(currentTile);
    }

    private void UpdateLocalFollow(double separation)
    {
        if (separation <= MaximumFollowDistance)
        {
            if (navigating)
                StopNavigation();
            ResetLeaderTrail();
            return;
        }

        if (navigating)
        {
            npc.speed = GetFollowSpeed(separation);
            NpcNavigationStatus status = navigation.Update(npc);
            if (status == NpcNavigationStatus.Moving)
                return;

            StopNavigation();
            if (status == NpcNavigationStatus.Reached)
                ConsumeActiveTrailWaypoint();
            else
                pathRetryTicks = PathRetryTicks;
        }

        if (npc.temporaryController is not null)
            throw new InvalidOperationException("npc_became_busy");
        if (pathRetryTicks > 0)
        {
            pathRetryTicks--;
            return;
        }
        if (separation < FollowStartDistance)
            return;

        bool usesTrail = TrySelectTrailWaypoint(out Point waypoint, out int trailPoints);
        bool foundPath = usesTrail
            ? pathfinder.TryFindFollowPath(
                leader.currentLocation,
                npc,
                npc.TilePoint,
                waypoint,
                out IReadOnlyList<Point> path)
            : pathfinder.TryFindPathToFollowRange(
                leader.currentLocation,
                npc,
                npc.TilePoint,
                leader.TilePoint,
                MinimumFollowDistance,
                MaximumFollowDistance,
                out path);
        if (!foundPath)
        {
            pathRetryTicks = PathRetryTicks;
            return;
        }
        if (path.Count <= 1)
        {
            if (usesTrail)
                RemoveTrailPoints(trailPoints);
            return;
        }

        activeWaypointUsesTrail = usesTrail;
        activeTrailPoints = usesTrail ? trailPoints : 0;
        npc.speed = GetFollowSpeed(separation);
        navigation.Start(path, npc);
        navigating = true;
    }

    private bool TrySelectTrailWaypoint(out Point waypoint, out int trailPoints)
    {
        while (leaderTrail.Count > TrailLagTiles
               && TileDistance(npc.TilePoint, leaderTrail[0]) <= 1d)
        {
            leaderTrail.RemoveAt(0);
        }

        int availableTrailPoints = leaderTrail.Count - TrailLagTiles;
        if (availableTrailPoints <= 0)
        {
            waypoint = Point.Zero;
            trailPoints = 0;
            return false;
        }

        trailPoints = Math.Min(availableTrailPoints, TrailWaypointSpan);
        waypoint = leaderTrail[trailPoints - 1];
        return true;
    }

    private void ConsumeActiveTrailWaypoint()
    {
        if (activeWaypointUsesTrail)
            RemoveTrailPoints(activeTrailPoints);
        activeWaypointUsesTrail = false;
        activeTrailPoints = 0;
    }

    private void RemoveTrailPoints(int count)
    {
        count = Math.Clamp(count, 0, leaderTrail.Count);
        if (count > 0)
            leaderTrail.RemoveRange(0, count);
    }

    private bool UpdateDistanceWarning(double separation)
    {
        if (distanceWarningCooldownTicks > 0)
            distanceWarningCooldownTicks--;
        if (separation <= WaitWarningResetDistance)
            distanceWarningArmed = true;
        if (!distanceWarningArmed
            || distanceWarningCooldownTicks > 0
            || separation < WaitWarningDistance
            || Game1.activeClickableMenu is not null
            || Game1.dialogueUp)
        {
            return false;
        }

        npc.showTextAboveHead("等等我，别走那么快！");
        distanceWarningArmed = false;
        distanceWarningCooldownTicks = WaitWarningCooldownTicks;
        barkCooldownTicks = Math.Max(barkCooldownTicks, 180);
        monitor.Log(
            $"move_to wait_warning npc={npc.Name} distance={separation:F1} destination={destination.Key}.",
            LogLevel.Debug);
        return true;
    }

    private void UpdateTravelBark()
    {
        if (travelBarks.Count == 0)
            return;
        if (barkCooldownTicks > 0)
        {
            barkCooldownTicks--;
            return;
        }
        if (Game1.activeClickableMenu is not null || Game1.dialogueUp)
            return;

        npc.showTextAboveHead(travelBarks.Dequeue());
        barkCooldownTicks = BarkCooldownTicks;
    }

    private void UpdateArrival(double separation)
    {
        if (stage != TravelStage.EnRoute)
            return;

        arrivalTicks = IsDestinationLocation(leader.currentLocation) && separation <= ArrivalDistance
            ? arrivalTicks + 1
            : 0;
        if (arrivalTicks < ArrivalHoldTicks)
            return;

        stage = TravelStage.AtDestination;
        destinationExitTicks = 0;
        onDestinationReached();
        monitor.Log(
            $"move_to destination_reached npc={npc.Name} target={leader.currentLocation.NameOrUniqueName} "
            + $"npc_tile={npc.TilePoint.X},{npc.TilePoint.Y} leader_tile={leader.TilePoint.X},{leader.TilePoint.Y}.",
            LogLevel.Info);
    }

    private bool IsDestinationLocation(GameLocation location)
        => location.NameOrUniqueName.Equals(
            destination.TargetLocationName,
            StringComparison.OrdinalIgnoreCase);

    private void UpdateDestinationExit()
    {
        StopNavigation();
        arrivalTicks = 0;
        destinationExitTicks++;
        if (destinationExitTicks == 1)
        {
            monitor.Log(
                $"move_to destination_exit_detected npc={npc.Name} destination={destination.Key} "
                + $"leader_location={leader.currentLocation.NameOrUniqueName} grace_ticks={DestinationExitGraceTicks}.",
                LogLevel.Info);
        }
        if (destinationExitTicks < DestinationExitGraceTicks)
            return;

        ReleaseToSchedule();
    }

    private void ReleaseToSchedule()
    {
        StopNavigation();
        scheduleRecovery.Release(npc, originalIgnoreScheduleToday, "move_to");
        stage = TravelStage.Released;
        onSessionEnded();
    }

    private bool TryResumeVanillaSchedule()
    {
        Dictionary<int, SchedulePathDescription>? schedule = npc.Schedule;
        if (schedule is null || schedule.Count == 0)
            return false;

        int currentTime = Game1.timeOfDay;
        int? nextTime = schedule.Keys
            .Where(time => time > currentTime)
            .OrderBy(time => time)
            .Cast<int?>()
            .FirstOrDefault();
        if (!nextTime.HasValue)
            return false;

        int? catchUpTime = schedule.Keys
            .Where(time => time <= currentTime)
            .OrderByDescending(time => time)
            .Cast<int?>()
            .FirstOrDefault();
        if (catchUpTime.HasValue
            && schedule.TryGetValue(catchUpTime.Value, out SchedulePathDescription? catchUpTarget)
            && TryRecalculateSchedulePath(catchUpTarget, currentTime, out SchedulePathDescription? catchUpPath))
        {
            schedule[currentTime] = catchUpPath;
            npc.queuedSchedulePaths.Clear();
            npc.lastAttemptedSchedule = PreviousTenMinuteTime(currentTime);
            npc.checkSchedule(currentTime);
            monitor.Log(
                $"move_to released_to_schedule npc={npc.Name} mode=catch_up trigger={currentTime} "
                + $"target={catchUpPath.targetLocationName}:{catchUpPath.targetTile.X},{catchUpPath.targetTile.Y} "
                + $"next_time={nextTime.Value}.",
                LogLevel.Info);
            return true;
        }

        if (!schedule.TryGetValue(nextTime.Value, out SchedulePathDescription? nextTarget)
            || !TryRecalculateSchedulePath(nextTarget, nextTime.Value, out SchedulePathDescription? nextPath))
        {
            monitor.Log(
                $"move_to schedule_resume_failed npc={npc.Name} next_time={nextTime.Value} "
                + $"location={npc.currentLocation.NameOrUniqueName}.",
                LogLevel.Warn);
            return false;
        }

        schedule[nextTime.Value] = nextPath;
        npc.queuedSchedulePaths.Clear();
        monitor.Log(
            $"move_to released_to_schedule npc={npc.Name} mode=wait_for_next trigger={nextTime.Value} "
            + $"target={nextPath.targetLocationName}:{nextPath.targetTile.X},{nextPath.targetTile.Y}.",
            LogLevel.Info);
        return true;
    }

    private bool TryRecalculateSchedulePath(
        SchedulePathDescription target,
        int triggerTime,
        out SchedulePathDescription recalculated)
    {
        recalculated = null!;
        if (npc.TilePoint == Point.Zero
            || string.IsNullOrWhiteSpace(target.targetLocationName))
        {
            return false;
        }

        try
        {
            recalculated = npc.pathfindToNextScheduleLocation(
                "VivantValley-release",
                npc.currentLocation.NameOrUniqueName,
                npc.TilePoint.X,
                npc.TilePoint.Y,
                target.targetLocationName,
                target.targetTile.X,
                target.targetTile.Y,
                target.facingDirection,
                target.endOfRouteBehavior,
                target.endOfRouteMessage);
            recalculated.time = triggerTime;

            bool alreadyAtTarget = IsSameLocation(npc.currentLocation, target.targetLocationName)
                                   && npc.TilePoint == target.targetTile;
            return recalculated.route is not null
                   && (recalculated.route.Count > 0 || alreadyAtTarget);
        }
        catch (Exception exception)
        {
            monitor.Log(
                $"move_to schedule_path_failed npc={npc.Name} target={target.targetLocationName}:"
                + $"{target.targetTile.X},{target.targetTile.Y} reason={CleanReason(exception.Message, "path_not_found")}.",
                LogLevel.Debug);
            return false;
        }
    }

    private bool TryStartVanillaHomeRoute()
    {
        GameLocation? home = npc.getHome();
        if (home is null || npc.TilePoint == Point.Zero)
            return false;

        Point homeTile = new(
            (int)npc.DefaultPosition.X / Game1.tileSize,
            (int)npc.DefaultPosition.Y / Game1.tileSize);
        if (homeTile == Point.Zero)
            return false;
        if (IsSameLocation(npc.currentLocation, home.NameOrUniqueName) && npc.TilePoint == homeTile)
        {
            monitor.Log(
                $"move_to released_returning_home npc={npc.Name} mode=already_home "
                + $"target={home.NameOrUniqueName}:{homeTile.X},{homeTile.Y}.",
                LogLevel.Info);
            return true;
        }

        try
        {
            SchedulePathDescription route = npc.pathfindToNextScheduleLocation(
                "VivantValley-home",
                npc.currentLocation.NameOrUniqueName,
                npc.TilePoint.X,
                npc.TilePoint.Y,
                home.NameOrUniqueName,
                homeTile.X,
                homeTile.Y,
                2,
                null!,
                null!);
            if (route.route is null || route.route.Count == 0)
                return false;

            npc.queuedSchedulePaths.Clear();
            npc.controller = new PathFindController(route.route, npc, npc.currentLocation)
            {
                finalFacingDirection = 2,
                NPCSchedule = true,
            };
            monitor.Log(
                $"move_to released_returning_home npc={npc.Name} mode=vanilla_route "
                + $"source={npc.currentLocation.NameOrUniqueName} target={home.NameOrUniqueName}:"
                + $"{homeTile.X},{homeTile.Y}.",
                LogLevel.Info);
            return true;
        }
        catch (Exception exception)
        {
            monitor.Log(
                $"move_to home_route_failed npc={npc.Name} source={npc.currentLocation.NameOrUniqueName} "
                + $"target={home.NameOrUniqueName} reason={CleanReason(exception.Message, "path_not_found")}.",
                LogLevel.Warn);
            return false;
        }
    }

    private bool TryWarpHomeOffscreen()
    {
        GameLocation? home = npc.getHome();
        if (home is null)
            return false;

        bool locationIsObserved = Game1.getAllFarmers().Any(farmer =>
            ReferenceEquals(farmer.currentLocation, npc.currentLocation)
            || farmer.currentLocation.NameOrUniqueName.Equals(
                npc.currentLocation.NameOrUniqueName,
                StringComparison.OrdinalIgnoreCase));
        if (locationIsObserved)
        {
            monitor.Log(
                $"move_to home_warp_suppressed npc={npc.Name} location={npc.currentLocation.NameOrUniqueName} "
                + $"reason=player_present.",
                LogLevel.Warn);
            return false;
        }

        Point homeTile = new(
            (int)npc.DefaultPosition.X / Game1.tileSize,
            (int)npc.DefaultPosition.Y / Game1.tileSize);
        if (homeTile == Point.Zero)
            return false;
        GameLocation source = npc.currentLocation;
        Game1.warpCharacter(npc, home, new Vector2(homeTile.X, homeTile.Y));
        npc.faceDirection(2);
        monitor.Log(
            $"move_to released_returning_home npc={npc.Name} mode=offscreen_fallback "
            + $"source={source.NameOrUniqueName} target={home.NameOrUniqueName}:{homeTile.X},{homeTile.Y}.",
            LogLevel.Warn);
        return true;
    }

    private static bool IsSameLocation(GameLocation location, string locationName)
        => location.NameOrUniqueName.Equals(locationName, StringComparison.OrdinalIgnoreCase)
           || location.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase);

    private static int PreviousTenMinuteTime(int time)
    {
        int hour = time / 100;
        int minute = (time % 100) - 10;
        if (minute < 0)
        {
            hour--;
            minute = 50;
        }
        return (hour * 100) + minute;
    }

    private void ResetLeaderTrail()
    {
        leaderTrail.Clear();
        lastLeaderTile = leader.TilePoint;
        leaderTrail.Add(lastLeaderTile);
        activeWaypointUsesTrail = false;
        activeTrailPoints = 0;
    }

    private int GetFollowSpeed(double separation)
    {
        int desiredSpeed = separation >= WaitWarningDistance
            ? 7
            : separation >= CatchUpDistance
                ? 6
                : 5;
        return Math.Max(originalSpeed, desiredSpeed);
    }

    private void StopNavigation()
    {
        navigation.Stop(npc);
        navigating = false;
        npc.speed = originalSpeed;
    }

    private void ReleaseNpc()
    {
        StopNavigation();
        npc.ignoreScheduleToday = originalIgnoreScheduleToday;
    }

    private void Fail(string reason)
    {
        ReleaseNpc();
        FailureReason = reason;
        stage = TravelStage.Failed;
        onSessionEnded();
        monitor.Log(
            $"move_to failed npc={npc.Name} destination={destination.Key} reason={reason}.",
            LogLevel.Warn);
    }

    private static string CleanBark(string? value)
    {
        string clean = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= 120 ? clean : clean[..120];
    }

    private static string CleanReason(string? value, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (clean.Length == 0)
            return fallback;
        return clean.Length <= 160 ? clean : clean[..160];
    }

    private static double TileDistance(Point first, Point second)
        => Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));
}
