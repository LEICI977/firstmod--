using Microsoft.Xna.Framework;
using StardewValley;

namespace VivantValley.Services;

internal sealed record NpcExitApproach(
    Point Tile,
    Point TriggerDirection,
    IReadOnlyList<Point> Path);

/// <summary>Finds one-map NPC paths using the same collision probe as the game controller.</summary>
internal sealed class NpcTilePathfinder
{
    private const int MaximumVisitedTiles = 30_000;
    private const int MaximumFollowVisitedTiles = 4_000;

    private static readonly Point[] Directions =
    {
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0),
    };

    public bool TryFindPath(
        GameLocation location,
        NPC npc,
        Point start,
        Point goal,
        out IReadOnlyList<Point> path,
        bool allowGoalTransition = false)
    {
        List<Point>? found = FindPath(location, npc, start, goal, allowGoalTransition);
        path = found is null ? Array.Empty<Point>() : found;
        return found is not null;
    }

    public bool TryFindFollowPath(
        GameLocation location,
        NPC npc,
        Point start,
        Point goal,
        out IReadOnlyList<Point> path)
    {
        List<Point>? found = FindPath(
            location,
            npc,
            start,
            goal,
            allowGoalTransition: false,
            maximumVisitedTiles: MaximumFollowVisitedTiles);
        path = found is null ? Array.Empty<Point>() : found;
        return found is not null;
    }

    public bool TryFindPathToAdjacent(
        GameLocation location,
        NPC npc,
        Point start,
        Point target,
        Point leashCenter,
        int maximumLeashDistance,
        out IReadOnlyList<Point> path,
        out Point standingTile)
    {
        maximumLeashDistance = Math.Max(1, maximumLeashDistance);
        Func<Point, bool> staysInsideLeash = tile => Manhattan(tile, leashCenter) <= maximumLeashDistance;
        var candidates = new List<(Point StandingTile, List<Point> Path)>();
        foreach (Point direction in Directions)
        {
            Point candidate = target + direction;
            if (!staysInsideLeash(candidate))
                continue;

            List<Point>? found = FindPath(
                location,
                npc,
                start,
                candidate,
                allowGoalTransition: false,
                maximumVisitedTiles: MaximumFollowVisitedTiles,
                allowedTile: staysInsideLeash);
            if (found is not null)
                candidates.Add((candidate, found));
        }

        (Point StandingTile, List<Point> Path) best = candidates
            .OrderBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.StandingTile.Y)
            .ThenBy(candidate => candidate.StandingTile.X)
            .FirstOrDefault();
        if (best.Path is null)
        {
            standingTile = Point.Zero;
            path = Array.Empty<Point>();
            return false;
        }

        standingTile = best.StandingTile;
        path = best.Path;
        return true;
    }

    public bool TryFindPathToFollowRange(
        GameLocation location,
        NPC npc,
        Point start,
        Point leaderTile,
        int minimumDistance,
        int maximumDistance,
        out IReadOnlyList<Point> path)
    {
        minimumDistance = Math.Max(1, minimumDistance);
        maximumDistance = Math.Max(minimumDistance, maximumDistance);
        HashSet<Point> transitionTiles = GetTransitionTiles(location);
        if (IsFollowTile(start, leaderTile, minimumDistance, maximumDistance, transitionTiles))
        {
            path = new[] { start };
            return true;
        }

        var frontier = new PriorityQueue<Point, int>();
        var cameFrom = new Dictionary<Point, Point>();
        var cost = new Dictionary<Point, int> { [start] = 0 };
        frontier.Enqueue(start, 0);
        for (int visited = 0; frontier.Count > 0 && visited < MaximumFollowVisitedTiles; visited++)
        {
            Point current = frontier.Dequeue();
            if (current != start
                && IsFollowTile(current, leaderTile, minimumDistance, maximumDistance, transitionTiles))
            {
                path = Reconstruct(cameFrom, start, current);
                return true;
            }

            foreach (Point direction in Directions)
            {
                Point next = current + direction;
                if (!IsWalkable(
                        location,
                        npc,
                        next,
                        start,
                        leaderTile,
                        allowGoalTransition: false,
                        transitionTiles))
                {
                    continue;
                }

                int nextCost = cost[current] + 1;
                if (cost.TryGetValue(next, out int knownCost) && knownCost <= nextCost)
                    continue;

                cost[next] = nextCost;
                cameFrom[next] = current;
                int distanceToRange = Math.Max(0, Manhattan(next, leaderTile) - maximumDistance);
                frontier.Enqueue(next, nextCost + distanceToRange);
            }
        }

        path = Array.Empty<Point>();
        return false;
    }

    public bool TryFindSafeFollowTile(
        GameLocation location,
        NPC npc,
        Point leaderTile,
        int minimumDistance,
        int maximumDistance,
        out Point tile)
    {
        minimumDistance = Math.Max(1, minimumDistance);
        maximumDistance = Math.Max(minimumDistance, maximumDistance);
        HashSet<Point> transitionTiles = GetTransitionTiles(location);
        Point collisionProbeStart = new(-100_000, -100_000);
        for (int distance = minimumDistance; distance <= maximumDistance; distance++)
        {
            foreach (Point candidate in EnumerateRing(leaderTile, distance))
            {
                if (IsWalkable(
                        location,
                        npc,
                        candidate,
                        collisionProbeStart,
                        leaderTile,
                        allowGoalTransition: false,
                        transitionTiles))
                {
                    tile = candidate;
                    return true;
                }
            }
        }

        tile = Point.Zero;
        return false;
    }

    public bool TryFindExitApproach(
        GameLocation location,
        NPC npc,
        Point start,
        Point exitTile,
        out NpcExitApproach? approach)
    {
        int width = location.Map.Layers[0].LayerWidth;
        int height = location.Map.Layers[0].LayerHeight;
        approach = ListExitApproaches(width, height, exitTile)
            .Select(candidate => new
            {
                Candidate = candidate,
                Path = FindPath(
                    location,
                    npc,
                    start,
                    candidate.Tile,
                    allowGoalTransition: candidate.TriggerDirection == Point.Zero),
            })
            .Where(candidate => candidate.Path is not null)
            .OrderByDescending(candidate => candidate.Candidate.TriggerDirection != Point.Zero)
            .ThenBy(candidate => candidate.Path!.Count)
            .Select(candidate => new NpcExitApproach(
                candidate.Candidate.Tile,
                candidate.Candidate.TriggerDirection,
                candidate.Path!))
            .FirstOrDefault();
        return approach is not null;
    }

    public bool TryFindSafeArrival(
        GameLocation location,
        NPC npc,
        Point arrival,
        out Point destination,
        out IReadOnlyList<Point> path,
        int maximumRadius = 6)
    {
        maximumRadius = Math.Max(1, maximumRadius);
        HashSet<Point> transitionTiles = GetTransitionTiles(location);
        var frontier = new Queue<Point>();
        var cameFrom = new Dictionary<Point, Point>();
        var visited = new HashSet<Point> { arrival };
        frontier.Enqueue(arrival);

        while (frontier.Count > 0)
        {
            Point current = frontier.Dequeue();
            if (current != arrival)
            {
                destination = current;
                path = Reconstruct(cameFrom, arrival, current);
                return true;
            }

            foreach (Point direction in Directions)
            {
                Point next = current + direction;
                if (Manhattan(arrival, next) > maximumRadius || !visited.Add(next))
                    continue;
                if (!IsWalkable(
                        location,
                        npc,
                        next,
                        arrival,
                        next,
                        allowGoalTransition: false,
                        transitionTiles))
                {
                    continue;
                }

                cameFrom[next] = current;
                frontier.Enqueue(next);
            }
        }

        destination = Point.Zero;
        path = Array.Empty<Point>();
        return false;
    }

    private List<Point>? FindPath(
        GameLocation location,
        NPC npc,
        Point start,
        Point goal,
        bool allowGoalTransition,
        int maximumVisitedTiles = MaximumVisitedTiles,
        Func<Point, bool>? allowedTile = null)
    {
        if (start == goal)
            return new List<Point> { start };
        if (allowedTile is not null && (!allowedTile(start) || !allowedTile(goal)))
            return null;
        HashSet<Point> transitionTiles = GetTransitionTiles(location);
        if (!IsWalkable(location, npc, goal, start, goal, allowGoalTransition, transitionTiles))
            return null;

        var frontier = new PriorityQueue<Point, int>();
        var cameFrom = new Dictionary<Point, Point>();
        var cost = new Dictionary<Point, int> { [start] = 0 };
        frontier.Enqueue(start, 0);
        for (int visited = 0; frontier.Count > 0 && visited < maximumVisitedTiles; visited++)
        {
            Point current = frontier.Dequeue();
            if (current == goal)
                return Reconstruct(cameFrom, start, goal);

            foreach (Point direction in Directions)
            {
                Point next = current + direction;
                if ((allowedTile is not null && !allowedTile(next))
                    || !IsWalkable(location, npc, next, start, goal, allowGoalTransition, transitionTiles))
                    continue;

                int nextCost = cost[current] + 1;
                if (cost.TryGetValue(next, out int knownCost) && knownCost <= nextCost)
                    continue;

                cost[next] = nextCost;
                cameFrom[next] = current;
                frontier.Enqueue(next, nextCost + Manhattan(next, goal));
            }
        }

        return null;
    }

    private static bool IsWalkable(
        GameLocation location,
        NPC npc,
        Point tile,
        Point start,
        Point goal,
        bool allowGoalTransition,
        IReadOnlySet<Point> transitionTiles)
    {
        if (tile == start)
            return true;
        if (!location.isTileOnMap(tile)
            || location.doesTileHaveProperty(tile.X, tile.Y, "NoPath", "Back") is not null
            || location.doesTileHaveProperty(tile.X, tile.Y, "NPCBarrier", "Back") is not null)
        {
            return false;
        }
        if ((!allowGoalTransition || tile != goal) && transitionTiles.Contains(tile))
        {
            return false;
        }

        Rectangle bounds = PathfindingBounds(tile);
        try
        {
            if (location.isCollidingPosition(
                    bounds,
                    Game1.viewport,
                    isFarmer: false,
                    damagesFarmer: 0,
                    glider: false,
                    character: npc,
                    pathfinding: true,
                    projectile: false,
                    ignoreCharacterRequirement: false,
                    skipCollisionEffects: true))
            {
                return false;
            }

            if (location.characters.Any(other =>
                    !ReferenceEquals(other, npc)
                    && !other.IsInvisible
                    && other.GetBoundingBox().Intersects(bounds)))
            {
                return false;
            }

            return !location.farmers.Any(farmer => farmer.GetBoundingBox().Intersects(bounds));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsFollowTile(
        Point tile,
        Point leaderTile,
        int minimumDistance,
        int maximumDistance,
        IReadOnlySet<Point> transitionTiles)
    {
        int distance = Manhattan(tile, leaderTile);
        return distance >= minimumDistance
               && distance <= maximumDistance
               && !transitionTiles.Contains(tile);
    }

    private static IEnumerable<Point> EnumerateRing(Point center, int distance)
    {
        for (int xOffset = -distance; xOffset <= distance; xOffset++)
        {
            int yOffset = distance - Math.Abs(xOffset);
            yield return new Point(center.X + xOffset, center.Y + yOffset);
            if (yOffset != 0)
                yield return new Point(center.X + xOffset, center.Y - yOffset);
        }
    }

    internal static Rectangle PathfindingBounds(Point tile)
        => new(
            (tile.X * Game1.tileSize) + 1,
            (tile.Y * Game1.tileSize) + 1,
            Game1.tileSize - 2,
            Game1.tileSize - 2);

    private static HashSet<Point> GetTransitionTiles(GameLocation location)
    {
        var result = location.warps
            .Select(warp => new Point(warp.X, warp.Y))
            .ToHashSet();
        result.UnionWith(location.doors.Pairs.Select(pair => pair.Key));
        result.UnionWith(location.buildings
            .Where(building => building.HasIndoors())
            .Select(building => building.getPointForHumanDoor()));
        return result;
    }

    private static IReadOnlyList<(Point Tile, Point TriggerDirection)> ListExitApproaches(
        int width,
        int height,
        Point exitTile)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (exitTile.Y < 0)
            return new[] { (new Point(Math.Clamp(exitTile.X, 0, width - 1), 0), new Point(0, -1)) };
        if (exitTile.X >= width)
            return new[] { (new Point(width - 1, Math.Clamp(exitTile.Y, 0, height - 1)), new Point(1, 0)) };
        if (exitTile.Y >= height)
            return new[] { (new Point(Math.Clamp(exitTile.X, 0, width - 1), height - 1), new Point(0, 1)) };
        if (exitTile.X < 0)
            return new[] { (new Point(0, Math.Clamp(exitTile.Y, 0, height - 1)), new Point(-1, 0)) };

        return new[] { (exitTile, Point.Zero) }
            .Concat(Directions.Select(direction => (exitTile - direction, direction)))
            .ToArray();
    }

    private static List<Point> Reconstruct(
        IReadOnlyDictionary<Point, Point> cameFrom,
        Point start,
        Point goal)
    {
        var path = new List<Point> { goal };
        Point current = goal;
        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    private static int Manhattan(Point first, Point second)
        => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);
}
