using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace VivantValley.Services;

internal enum NpcNavigationStatus
{
    Moving,
    Reached,
    Blocked,
}

/// <summary>Runs and monitors one collision-checked NPC path within a single location.</summary>
internal sealed class NpcNavigationController
{
    private const int StationaryTickLimit = 90;
    private const int NoProgressTickLimit = 300;
    private const int RepeatedTileLimit = 4;

    private PathFindController? controller;
    private Point destination;
    private Vector2 previousPosition;
    private Point previousTile;
    private int bestDistance;
    private int stationaryTicks;
    private int noProgressTicks;
    private int repeatedTileTransitions;
    private readonly HashSet<Point> visitedTiles = new();

    public void Start(IReadOnlyList<Point> path, NPC npc)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(npc);
        if (path.Count == 0)
            throw new ArgumentException("Navigation path cannot be empty.", nameof(path));

        Stop(npc);
        destination = path[^1];
        previousPosition = npc.Position;
        previousTile = npc.TilePoint;
        bestDistance = Manhattan(previousTile, destination);
        visitedTiles.Clear();
        visitedTiles.Add(previousTile);
        stationaryTicks = 0;
        noProgressTicks = 0;
        repeatedTileTransitions = 0;
        if (path.Count == 1 || previousTile == destination)
            return;

        var route = new Stack<Point>();
        for (int index = path.Count - 1; index >= 0; index--)
            route.Push(path[index]);

        controller = new PathFindController(route, npc.currentLocation, npc, destination)
        {
            finalFacingDirection = -1,
            // This controller owns only local following. The travel session handles
            // player-led map transitions separately.
            nonDestructivePathing = false,
            // The vanilla controller teleports non-schedule characters directly to the
            // endpoint whenever no player is present in that map. Schedule mode keeps
            // follower movement running at normal per-tick speed.
            NPCSchedule = true,
        };
        npc.temporaryController = controller;
    }

    public NpcNavigationStatus Update(NPC npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (npc.TilePoint == destination)
        {
            Stop(npc);
            return NpcNavigationStatus.Reached;
        }

        if (controller is null || !ReferenceEquals(npc.temporaryController, controller))
        {
            Stop(npc);
            return NpcNavigationStatus.Blocked;
        }

        int distance = Manhattan(npc.TilePoint, destination);
        bool changedTile = npc.TilePoint != previousTile;
        bool reachedCloserTile = distance < bestDistance;
        if (reachedCloserTile)
            bestDistance = distance;

        bool reachedNewTile = changedTile && visitedTiles.Add(npc.TilePoint);
        if (changedTile && !reachedNewTile)
            repeatedTileTransitions++;

        stationaryTicks = Vector2.DistanceSquared(npc.Position, previousPosition) < 0.25f
            ? stationaryTicks + 1
            : 0;
        noProgressTicks = reachedCloserTile || reachedNewTile
            ? 0
            : noProgressTicks + 1;
        previousPosition = npc.Position;
        previousTile = npc.TilePoint;

        if (stationaryTicks >= StationaryTickLimit
            || noProgressTicks >= NoProgressTickLimit
            || repeatedTileTransitions >= RepeatedTileLimit)
        {
            Stop(npc);
            return NpcNavigationStatus.Blocked;
        }

        return NpcNavigationStatus.Moving;
    }

    public void Stop(NPC npc)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (controller is not null && ReferenceEquals(npc.temporaryController, controller))
            npc.temporaryController = null;
        if (controller is not null && ReferenceEquals(npc.controller, controller))
            npc.controller = null;

        npc.Halt();
        controller = null;
        stationaryTicks = 0;
        noProgressTicks = 0;
        repeatedTileTransitions = 0;
        visitedTiles.Clear();
    }

    private static int Manhattan(Point first, Point second)
        => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
}
