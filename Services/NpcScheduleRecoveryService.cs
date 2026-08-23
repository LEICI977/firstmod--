using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;

namespace VivantValley.Services;

/// <summary>Restores a villager's vanilla schedule after a player-led session ends.</summary>
internal sealed class NpcScheduleRecoveryService
{
    private readonly IMonitor monitor;

    public NpcScheduleRecoveryService(IMonitor monitor)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    /// <summary>
    /// Release the NPC back to its schedule, or start a safe return-home route when
    /// there is no schedule node that can be resumed today.
    /// </summary>
    public bool Release(NPC npc, bool originalIgnoreScheduleToday, string source)
    {
        ArgumentNullException.ThrowIfNull(npc);
        StopControllers(npc);
        npc.ignoreScheduleToday = originalIgnoreScheduleToday;

        if (!originalIgnoreScheduleToday && TryResumeVanillaSchedule(npc, source))
            return true;

        if (TryStartVanillaHomeRoute(npc, source))
            return true;

        bool warpedHome = TryWarpHomeOffscreen(npc, source);
        if (!warpedHome)
        {
            monitor.Log(
                $"{source} released_in_place npc={npc.Name} location={npc.currentLocation?.NameOrUniqueName ?? "unknown"} "
                + "reason=home_route_unavailable_or_observed.",
                LogLevel.Warn);
        }

        return warpedHome;
    }

    private static void StopControllers(NPC npc)
    {
        npc.controller = null;
        npc.temporaryController = null;
        npc.queuedSchedulePaths.Clear();
        npc.Halt();
    }

    private bool TryResumeVanillaSchedule(NPC npc, string source)
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
            && TryRecalculateSchedulePath(npc, catchUpTarget, currentTime, out SchedulePathDescription? catchUpPath, source))
        {
            schedule[currentTime] = catchUpPath;
            npc.queuedSchedulePaths.Clear();
            npc.lastAttemptedSchedule = PreviousTenMinuteTime(currentTime);
            npc.checkSchedule(currentTime);
            monitor.Log(
                $"{source} released_to_schedule npc={npc.Name} mode=catch_up trigger={currentTime} "
                + $"target={catchUpPath.targetLocationName}:{catchUpPath.targetTile.X},{catchUpPath.targetTile.Y} "
                + $"next_time={nextTime.Value}.",
                LogLevel.Info);
            return true;
        }

        if (!schedule.TryGetValue(nextTime.Value, out SchedulePathDescription? nextTarget)
            || !TryRecalculateSchedulePath(npc, nextTarget, nextTime.Value, out SchedulePathDescription? nextPath, source))
        {
            monitor.Log(
                $"{source} schedule_resume_failed npc={npc.Name} next_time={nextTime.Value} "
                + $"location={npc.currentLocation?.NameOrUniqueName ?? "unknown"}.",
                LogLevel.Warn);
            return false;
        }

        schedule[nextTime.Value] = nextPath;
        npc.queuedSchedulePaths.Clear();
        monitor.Log(
            $"{source} released_to_schedule npc={npc.Name} mode=wait_for_next trigger={nextTime.Value} "
            + $"target={nextPath.targetLocationName}:{nextPath.targetTile.X},{nextPath.targetTile.Y}.",
            LogLevel.Info);
        return true;
    }

    private bool TryRecalculateSchedulePath(
        NPC npc,
        SchedulePathDescription target,
        int triggerTime,
        out SchedulePathDescription recalculated,
        string source)
    {
        recalculated = null!;
        if (npc.TilePoint == Point.Zero
            || string.IsNullOrWhiteSpace(target.targetLocationName)
            || npc.currentLocation is null)
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
                $"{source} schedule_path_failed npc={npc.Name} target={target.targetLocationName}:"
                + $"{target.targetTile.X},{target.targetTile.Y} reason={CleanReason(exception.Message, "path_not_found") }.",
                LogLevel.Debug);
            return false;
        }
    }

    private bool TryStartVanillaHomeRoute(NPC npc, string source)
    {
        GameLocation? home = npc.getHome();
        if (home is null || npc.currentLocation is null || npc.TilePoint == Point.Zero)
            return false;

        Point homeTile = new(
            (int)npc.DefaultPosition.X / Game1.tileSize,
            (int)npc.DefaultPosition.Y / Game1.tileSize);
        if (homeTile == Point.Zero)
            return false;
        if (IsSameLocation(npc.currentLocation, home.NameOrUniqueName) && npc.TilePoint == homeTile)
        {
            monitor.Log(
                $"{source} released_returning_home npc={npc.Name} mode=already_home "
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
                $"{source} released_returning_home npc={npc.Name} mode=vanilla_route "
                + $"source={npc.currentLocation.NameOrUniqueName} target={home.NameOrUniqueName}:"
                + $"{homeTile.X},{homeTile.Y}.",
                LogLevel.Info);
            return true;
        }
        catch (Exception exception)
        {
            monitor.Log(
                $"{source} home_route_failed npc={npc.Name} source={npc.currentLocation.NameOrUniqueName} "
                + $"target={home.NameOrUniqueName} reason={CleanReason(exception.Message, "path_not_found")}.",
                LogLevel.Warn);
            return false;
        }
    }

    private bool TryWarpHomeOffscreen(NPC npc, string source)
    {
        GameLocation? home = npc.getHome();
        if (home is null || npc.currentLocation is null)
            return false;

        bool locationIsObserved = Game1.getAllFarmers().Any(farmer =>
            ReferenceEquals(farmer.currentLocation, npc.currentLocation)
            || farmer.currentLocation.NameOrUniqueName.Equals(
                npc.currentLocation.NameOrUniqueName,
                StringComparison.OrdinalIgnoreCase));
        if (locationIsObserved)
        {
            monitor.Log(
                $"{source} home_warp_suppressed npc={npc.Name} location={npc.currentLocation.NameOrUniqueName} "
                + "reason=player_present.",
                LogLevel.Warn);
            return false;
        }

        Point homeTile = new(
            (int)npc.DefaultPosition.X / Game1.tileSize,
            (int)npc.DefaultPosition.Y / Game1.tileSize);
        if (homeTile == Point.Zero)
            return false;
        GameLocation sourceLocation = npc.currentLocation;
        Game1.warpCharacter(npc, home, new Vector2(homeTile.X, homeTile.Y));
        npc.faceDirection(2);
        monitor.Log(
            $"{source} released_returning_home npc={npc.Name} mode=offscreen_fallback "
            + $"source={sourceLocation.NameOrUniqueName} target={home.NameOrUniqueName}:{homeTile.X},{homeTile.Y}.",
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

    private static string CleanReason(string? value, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (clean.Length == 0)
            return fallback;
        return clean.Length <= 160 ? clean : clean[..160];
    }
}
