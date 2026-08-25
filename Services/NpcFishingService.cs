using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace VivantValley.Services;

/// <summary>Owns NPC fishing-companion sessions and their main-thread fishing actions.</summary>
public sealed class NpcFishingService
{
    private readonly IMonitor monitor;
    private readonly NpcCombatStateService combatState;
    private readonly ConversationSessionMemoryStore sessionMemory;
    private readonly NpcTilePathfinder pathfinder = new();
    private readonly NpcScheduleRecoveryService scheduleRecovery;
    private readonly Dictionary<string, NpcFishingSession> sessions = new(StringComparer.Ordinal);

    public NpcFishingService(
        IMonitor monitor,
        NpcCombatStateService combatState,
        ConversationSessionMemoryStore sessionMemory)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.combatState = combatState ?? throw new ArgumentNullException(nameof(combatState));
        this.sessionMemory = sessionMemory ?? throw new ArgumentNullException(nameof(sessionMemory));
        scheduleRecovery = new NpcScheduleRecoveryService(this.monitor);
    }

    public bool HasActiveSession(string? npcName)
        => !string.IsNullOrWhiteSpace(npcName) && sessions.ContainsKey(npcName);

    public string? GetAvailabilityReason(
        NPC npc,
        GameLocation? playerLocation,
        bool controlledByAnotherSession)
    {
        ArgumentNullException.ThrowIfNull(npc);
        if (!Game1.IsMasterGame)
            return "host_required";
        if (Game1.player is not Farmer leader
            || playerLocation is null
            || npc.currentLocation is null
            || !ReferenceEquals(leader.currentLocation, playerLocation)
            || !ReferenceEquals(npc.currentLocation, playerLocation))
        {
            return "npc_not_with_player";
        }
        if (!npc.IsVillager || npc.IsMonster || npc.IsInvisible || !npc.CanSocialize)
            return "npc_unavailable";
        if (combatState.IsHospitalized(npc.Name))
            return "npc_hospitalized";
        if (Game1.eventUp || Game1.isFestival() || playerLocation.currentEvent is not null)
            return "event_active";
        if (controlledByAnotherSession
            || sessions.ContainsKey(npc.Name)
            || npc.controller is not null
            || npc.temporaryController is not null)
        {
            return "npc_busy";
        }
        return null;
    }

    public ConversationFishingExecutionResult Execute(
        NPC npc,
        Farmer leader,
        bool controlledByAnotherSession)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(leader);
        string? reason = GetAvailabilityReason(npc, leader.currentLocation, controlledByAnotherSession);
        if (reason is not null)
            return Rejected(reason);

        try
        {
            sessions[npc.Name] = new NpcFishingSession(
                npc,
                leader,
                pathfinder,
                monitor,
                scheduleRecovery,
                onCatch: fishName => sessionMemory.RecordFishingCatch(
                    leader.UniqueMultiplayerID.ToString(CultureInfo.InvariantCulture),
                    npc.Name,
                    $"{Game1.Date} {Game1.timeOfDay}",
                    leader.currentLocation?.DisplayName
                    ?? leader.currentLocation?.NameOrUniqueName
                    ?? string.Empty,
                    fishName));
            monitor.Log(
                $"invite_fishing_companion session_started npc={npc.Name} leader={leader.UniqueMultiplayerID}.",
                LogLevel.Info);
            return new ConversationFishingExecutionResult
            {
                RequestedToolName = NpcFishingToolNames.InviteFishingCompanion,
                Outcome = ConversationFishingOutcome.Following,
            };
        }
        catch (Exception exception)
        {
            return new ConversationFishingExecutionResult
            {
                RequestedToolName = NpcFishingToolNames.InviteFishingCompanion,
                Outcome = ConversationFishingOutcome.Failed,
                FailureReason = CleanReason(exception.Message, "fishing_session_start_failed"),
            };
        }
    }

    public void Update()
    {
        foreach ((string npcName, NpcFishingSession session) in sessions.ToArray())
        {
            session.Update();
            if (session.IsComplete)
                sessions.Remove(npcName);
        }
    }

    public void DrawWorld(SpriteBatch spriteBatch)
    {
        foreach (NpcFishingSession session in sessions.Values)
            session.DrawWorld(spriteBatch);
    }

    public void CancelAll(string reason)
    {
        foreach (NpcFishingSession session in sessions.Values)
            session.Cancel(reason);
        sessions.Clear();
    }

    private static ConversationFishingExecutionResult Rejected(string reason)
        => new()
        {
            RequestedToolName = NpcFishingToolNames.InviteFishingCompanion,
            Outcome = ConversationFishingOutcome.Rejected,
            FailureReason = reason,
        };

    private static string CleanReason(string? value, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length == 0 ? fallback : clean.Length <= 160 ? clean : clean[..160];
    }
}

internal sealed class NpcFishingSession
{
    private const int CrossMapTransferDelayTicks = 90;
    private const int MinimumFollowDistance = 1;
    private const int MaximumFollowDistance = 2;
    private const int FollowRetryTicks = 12;
    private const int CastWindupTicks = 12;
    private const int CastReleaseTicks = 10;
    private const int BobberLandingTicks = 8;
    private const int BiteReactionTicks = 8;
    private const int ReelWindupTicks = 8;
    private const int ReelAnimationTicks = 28;
    private const int HoldCatchTicks = 72;
    private const int MaximumCastDistance = 7;

    private static readonly Point[] Directions =
    {
        new(0, -1),
        new(1, 0),
        new(0, 1),
        new(-1, 0),
    };

    private readonly NPC npc;
    private readonly Farmer leader;
    private readonly NpcTilePathfinder pathfinder;
    private readonly NpcNavigationController navigation = new();
    private readonly IMonitor monitor;
    private readonly NpcScheduleRecoveryService scheduleRecovery;
    private readonly Action<string> onCatch;
    private readonly FishingRod rodPreview;
    private readonly bool originalIgnoreScheduleToday;
    private readonly int originalSpeed;
    private FishingStage stage;
    private FishingSpot? fishingSpot;
    private Item? caughtItem;
    private int stageTicks;
    private int biteAtTicks;
    private int differentLocationTicks;
    private int pathRetryTicks;
    private bool navigating;
    private bool playerCastArmed;
    private bool pendingPlayerCast;
    private bool caughtAnyFish;
    private bool complete;
    private string lastFishingLocationName = string.Empty;

    public NpcFishingSession(
        NPC npc,
        Farmer leader,
        NpcTilePathfinder pathfinder,
        IMonitor monitor,
        NpcScheduleRecoveryService scheduleRecovery,
        Action<string> onCatch)
    {
        this.npc = npc;
        this.leader = leader;
        this.pathfinder = pathfinder;
        this.monitor = monitor;
        this.scheduleRecovery = scheduleRecovery;
        this.onCatch = onCatch;
        rodPreview = ItemRegistry.Create("(T)IridiumRod", 1, 0, allowNull: true) as FishingRod
                     ?? throw new InvalidOperationException("iridium_rod_unavailable");
        originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        originalSpeed = npc.speed;
        npc.ignoreScheduleToday = true;
        playerCastArmed = !IsPlayerFishing();
    }

    public bool IsComplete => complete;

    public void Update()
    {
        if (complete || !Context.IsWorldReady)
            return;
        if (leader.currentLocation is null
            || npc.currentLocation is null
            || npc.IsInvisible
            || !npc.CanSocialize
            || Game1.eventUp
            || Game1.isFestival()
            || leader.currentLocation.currentEvent is not null)
        {
            Finish("ended_npc_unavailable");
            return;
        }

        if (!ReferenceEquals(npc.currentLocation, leader.currentLocation))
        {
            if (caughtAnyFish
                && !leader.currentLocation.NameOrUniqueName.Equals(lastFishingLocationName, StringComparison.OrdinalIgnoreCase))
            {
                Finish("ended_player_left_fishing_location");
                return;
            }

            UpdateCrossMapTransfer();
            return;
        }

        differentLocationTicks = 0;
        bool playerFishing = IsPlayerFishing();
        if (!playerFishing)
            playerCastArmed = true;
        else if (playerCastArmed && stage == FishingStage.Ready)
        {
            playerCastArmed = false;
            pendingPlayerCast = true;
        }

        if (stage != FishingStage.Ready)
        {
            if (TileDistance(npc.TilePoint, leader.TilePoint) > 3)
            {
                AbortFishingAction("player_moved_out_of_range");
                UpdateFollow();
                return;
            }

            UpdateFishingAction();
            return;
        }

        if (pendingPlayerCast && TryStartFishingNearPlayer())
            return;

        UpdateFollow();
    }

    public void DrawWorld(SpriteBatch spriteBatch)
    {
        if (complete
            || stage == FishingStage.Ready
            || fishingSpot is null
            || npc.currentLocation is null
            || !ReferenceEquals(npc.currentLocation, Game1.currentLocation))
        {
            return;
        }

        Vector2 viewport = new(Game1.viewport.X, Game1.viewport.Y);
        FishingPose pose = GetFishingPose(viewport);
        if (stage != FishingStage.HoldingCatch)
            DrawFishingTool(spriteBatch, pose);

        if (stage != FishingStage.HoldingCatch)
        {
            Vector2 bobber = GetBobberScreenPosition(viewport, pose);
            DrawFishingLine(spriteBatch, pose.RodTip, bobber);
            DrawBobber(spriteBatch, bobber);
        }

        if (stage == FishingStage.HoldingCatch && caughtItem is not null)
        {
            float lift = 10f + MathF.Sin(stageTicks / 8f) * 3f;
            caughtItem.drawInMenu(
                spriteBatch,
                pose.Hand + new Vector2(-24f, -58f - lift),
                0.72f,
                1f,
                0.99f,
                StackDrawType.Hide,
                Color.White,
                false);
        }
    }

    public void Cancel(string reason)
    {
        if (!complete)
            Finish(reason);
    }

    private void UpdateCrossMapTransfer()
    {
        StopNavigation();
        AbortFishingAction("map_transfer");
        differentLocationTicks++;
        if (differentLocationTicks < CrossMapTransferDelayTicks)
            return;
        if (!pathfinder.TryFindSafeFollowTile(
                leader.currentLocation,
                npc,
                leader.TilePoint,
                MinimumFollowDistance,
                MaximumFollowDistance + 1,
                out Point arrivalTile))
        {
            return;
        }

        GameLocation source = npc.currentLocation;
        Game1.warpCharacter(npc, leader.currentLocation, new Vector2(arrivalTile.X, arrivalTile.Y));
        differentLocationTicks = 0;
        monitor.Log(
            $"invite_fishing_companion follower_transferred npc={npc.Name} source={source.NameOrUniqueName} "
            + $"target={leader.currentLocation.NameOrUniqueName} tile={arrivalTile.X},{arrivalTile.Y}.",
            LogLevel.Debug);
    }

    private void UpdateFollow()
    {
        double separation = TileDistance(npc.TilePoint, leader.TilePoint);
        if (separation <= MaximumFollowDistance)
        {
            if (navigating)
                StopNavigation();
            return;
        }

        if (navigating)
        {
            npc.speed = separation >= 5 ? 7 : 6;
            NpcNavigationStatus status = navigation.Update(npc);
            if (status == NpcNavigationStatus.Moving)
                return;
            StopNavigation();
            if (status == NpcNavigationStatus.Blocked)
                pathRetryTicks = FollowRetryTicks;
        }

        if (pathRetryTicks > 0)
        {
            pathRetryTicks--;
            return;
        }
        if (!pathfinder.TryFindPathToFollowRange(
                leader.currentLocation,
                npc,
                npc.TilePoint,
                leader.TilePoint,
                MinimumFollowDistance,
                MaximumFollowDistance,
                out IReadOnlyList<Point> path)
            || path.Count <= 1)
        {
            pathRetryTicks = FollowRetryTicks;
            return;
        }

        npc.speed = separation >= 5 ? 7 : 6;
        navigation.Start(path, npc);
        navigating = true;
    }

    private bool TryStartFishingNearPlayer()
    {
        if (TileDistance(npc.TilePoint, leader.TilePoint) > 3)
            return false;
        if (!TryFindFishingSpot(out FishingSpot? spot) || spot is null)
        {
            pendingPlayerCast = false;
            monitor.Log(
                $"invite_fishing_companion no_fishing_spot npc={npc.Name} location={leader.currentLocation.NameOrUniqueName}.",
                LogLevel.Debug);
            return false;
        }

        pendingPlayerCast = false;
        fishingSpot = spot;
        StopNavigation();
        if (spot.StandingTile != npc.TilePoint)
        {
            navigation.Start(spot.Path, npc);
            navigating = true;
            stage = FishingStage.MovingToSpot;
            return true;
        }

        BeginCast();
        return true;
    }

    private void UpdateFishingAction()
    {
        if (fishingSpot is null)
        {
            AbortFishingAction("spot_lost");
            return;
        }

        if (stage == FishingStage.MovingToSpot)
        {
            npc.speed = 6;
            NpcNavigationStatus status = navigation.Update(npc);
            if (status == NpcNavigationStatus.Moving)
                return;
            StopNavigation();
            if (status != NpcNavigationStatus.Reached || npc.TilePoint != fishingSpot.StandingTile)
            {
                AbortFishingAction("fishing_spot_blocked");
                return;
            }
            BeginCast();
            return;
        }

        if (npc.FacingDirection != fishingSpot.FacingDirection)
            npc.faceDirection(fishingSpot.FacingDirection);
        stageTicks++;
        switch (stage)
        {
            case FishingStage.CastWindup when stageTicks >= CastWindupTicks:
                stage = FishingStage.CastRelease;
                stageTicks = 0;
                break;
            case FishingStage.CastRelease when stageTicks >= CastReleaseTicks:
                stage = FishingStage.BobberLanding;
                stageTicks = 0;
                break;
            case FishingStage.BobberLanding when stageTicks >= BobberLandingTicks:
                stage = FishingStage.WaitingForBite;
                stageTicks = 0;
                biteAtTicks = Game1.random.Next(150, 361);
                break;
            case FishingStage.WaitingForBite when stageTicks >= biteAtTicks:
                stage = FishingStage.BiteReaction;
                stageTicks = 0;
                Game1.playSound("fishingRodBend");
                break;
            case FishingStage.BiteReaction when stageTicks >= BiteReactionTicks:
                stage = FishingStage.ReelWindup;
                stageTicks = 0;
                caughtItem = CreateCatch(fishingSpot);
                Game1.playSound("pullItemFromWater");
                break;
            case FishingStage.ReelWindup when stageTicks >= ReelWindupTicks:
                stage = FishingStage.Reeling;
                stageTicks = 0;
                break;
            case FishingStage.Reeling when stageTicks >= ReelAnimationTicks:
                stage = FishingStage.HoldingCatch;
                stageTicks = 0;
                if (caughtItem is not null)
                    npc.showTextAboveHead($"钓到了 {caughtItem.DisplayName}！");
                break;
            case FishingStage.HoldingCatch when stageTicks >= HoldCatchTicks:
                DeliverCatch();
                stage = FishingStage.Ready;
                stageTicks = 0;
                fishingSpot = null;
                caughtItem = null;
                break;
        }
    }

    private void BeginCast()
    {
        if (fishingSpot is null)
            return;
        stage = FishingStage.CastWindup;
        stageTicks = 0;
        npc.faceDirection(fishingSpot.FacingDirection);
        npc.Halt();
        lastFishingLocationName = npc.currentLocation.NameOrUniqueName;
        Game1.playSound("button1");
        monitor.Log(
            $"invite_fishing_companion cast_started npc={npc.Name} standing={npc.TilePoint.X},{npc.TilePoint.Y} "
            + $"bobber={fishingSpot.WaterTile.X},{fishingSpot.WaterTile.Y} depth={fishingSpot.WaterDepth}.",
            LogLevel.Debug);
    }

    private Item CreateCatch(FishingSpot spot)
    {
        try
        {
            Item? item = npc.currentLocation.getFish(
                Game1.random.Next(80, 420),
                string.Empty,
                spot.WaterDepth,
                leader,
                0d,
                new Vector2(spot.WaterTile.X, spot.WaterTile.Y));
            return item ?? ItemRegistry.Create("(O)168", 1);
        }
        catch (Exception exception)
        {
            monitor.Log(
                $"invite_fishing_companion catch_generation_failed npc={npc.Name} reason={exception.Message}.",
                LogLevel.Warn);
            return ItemRegistry.Create("(O)168", 1);
        }
    }

    private void DeliverCatch()
    {
        if (caughtItem is null)
            return;
        string displayName = caughtItem.DisplayName;
        bool stored = leader.couldInventoryAcceptThisItem(caughtItem)
                      && leader.addItemToInventoryBool(caughtItem, makeActiveObject: false);
        if (!stored)
            Game1.createItemDebris(caughtItem, leader.Position, -1, leader.currentLocation);

        caughtAnyFish = true;
        onCatch(displayName);
        monitor.Log(
            $"invite_fishing_companion catch_delivered npc={npc.Name} item={caughtItem.QualifiedItemId} "
            + $"display={displayName} delivery={(stored ? "inventory" : "debris")}.",
            LogLevel.Info);
    }

    private bool TryFindFishingSpot(out FishingSpot? bestSpot)
    {
        var spots = new List<FishingSpot>();
        IEnumerable<Point> standingTiles = new[] { npc.TilePoint }
            .Concat(EnumerateRing(leader.TilePoint, 1))
            .Concat(EnumerateRing(leader.TilePoint, 2))
            .Distinct();
        foreach (Point standing in standingTiles)
        {
            if (standing == leader.TilePoint
                || TileDistance(standing, leader.TilePoint) > MaximumFollowDistance
                || !pathfinder.TryFindPath(
                    leader.currentLocation,
                    npc,
                    npc.TilePoint,
                    standing,
                    out IReadOnlyList<Point> path))
            {
                continue;
            }

            for (int facing = 0; facing < Directions.Length; facing++)
            {
                if (TryFindCast(leader.currentLocation, standing, facing, out Point water, out int depth))
                    spots.Add(new FishingSpot(standing, water, facing, depth, path));
            }
        }

        bestSpot = spots
            .OrderByDescending(spot => spot.WaterDepth)
            .ThenBy(spot => spot.Path.Count)
            .ThenBy(spot => TileDistance(spot.StandingTile, leader.TilePoint))
            .FirstOrDefault();
        return bestSpot is not null;
    }

    private static bool TryFindCast(
        GameLocation location,
        Point standing,
        int facing,
        out Point water,
        out int depth)
    {
        Point offset = Directions[facing];
        Point? selected = null;
        int firstWaterDistance = 0;
        int selectedDistance = 0;
        for (int distance = 1; distance <= MaximumCastDistance; distance++)
        {
            Point candidate = standing + new Point(offset.X * distance, offset.Y * distance);
            if (!location.isTileOnMap(candidate))
                break;
            bool fishable;
            try
            {
                fishable = location.isTileFishable(candidate.X, candidate.Y);
            }
            catch
            {
                break;
            }
            if (!fishable)
            {
                if (firstWaterDistance > 0 || distance > 2)
                    break;
                continue;
            }

            if (firstWaterDistance == 0)
                firstWaterDistance = distance;
            selected = candidate;
            selectedDistance = distance;
        }

        if (!selected.HasValue || firstWaterDistance > 2)
        {
            water = Point.Zero;
            depth = 0;
            return false;
        }

        water = selected.Value;
        depth = Math.Max(1, selectedDistance - firstWaterDistance + 1);
        return true;
    }

    private FishingPose GetFishingPose(Vector2 viewport)
    {
        if (fishingSpot is null)
        {
            Vector2 fallback = npc.GetBoundingBox().Center.ToVector2() - viewport + new Vector2(0f, -18f);
            return new FishingPose(fallback, fallback, fallback, 0, SpriteEffects.None, Rectangle.Empty);
        }

        // NPC.Position is the same 64x64 foot anchor used by the vanilla Farmer
        // tool renderer. Keep the tool and line on this one shared coordinate set.
        Vector2 basePosition = npc.Position - viewport;
        int facing = fishingSpot.FacingDirection;
        int frame = GetFishingToolFrame();
        int y = facing is 0 or 2 ? 336 : 288;
        Rectangle source = new(frame * 48, y, 48, 48);
        SpriteEffects effects = facing == 3 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        Vector2 position = facing switch
        {
            0 => basePosition + (frame == 4 ? new Vector2(-80f, -96f) : new Vector2(-64f, -124f)),
            1 => basePosition + new Vector2(-64f, -160f),
            2 => basePosition + new Vector2(-64f, -124f),
            _ => basePosition + new Vector2(-64f, -160f),
        };
        if (facing == 0 && frame == 4)
            effects = SpriteEffects.FlipVertically;

        // Keep the hand on the NPC collision box, matching the character
        // renderer. The tool sprite and all line/bobber calculations share this
        // pose so they cannot drift apart when the NPC moves.
        Vector2 hand = npc.GetBoundingBox().Center.ToVector2() - viewport + new Vector2(0f, -18f);
        Vector2 direction = FacingVector(facing);
        float length = stage switch
        {
            FishingStage.CastWindup => 28f,
            FishingStage.CastRelease => 42f,
            FishingStage.BobberLanding => 48f,
            FishingStage.Reeling => 38f,
            _ => 52f,
        };
        Vector2 rodTip = hand + direction * length + new Vector2(0f, -18f);
        if (stage == FishingStage.CastWindup)
            rodTip -= direction * 18f;
        if (stage == FishingStage.BiteReaction)
            rodTip += new Vector2(0f, -MathF.Sin(stageTicks / (float)BiteReactionTicks * MathF.PI) * 14f);
        if (stage == FishingStage.ReelWindup)
            rodTip += new Vector2(0f, -MathF.Sin(stageTicks / (float)ReelWindupTicks * MathF.PI) * 18f);
        if (stage == FishingStage.Reeling)
            rodTip += new Vector2(0f, -MathF.Sin(stageTicks / (float)ReelAnimationTicks * MathF.PI) * 22f);
        return new FishingPose(hand, rodTip, position, frame, effects, source);
    }

    /// <summary>Draws the vanilla fishing tool-sheet poses without mutating Farmer state.</summary>
    private void DrawFishingTool(SpriteBatch spriteBatch, FishingPose pose)
    {
        if (fishingSpot is null)
            return;

        try
        {
            spriteBatch.Draw(
                Game1.toolSpriteSheet,
                pose.ToolPosition,
                pose.Source,
                rodPreview.getColor(),
                0f,
                Vector2.Zero,
                4f,
                pose.Effects,
                0.998f);
        }
        catch (Exception exception)
        {
            monitor.Log(
                $"invite_fishing_companion tool_draw_failed npc={npc.Name} reason={exception.Message}.",
                LogLevel.Debug);
        }
    }

    private int GetFishingToolFrame()
    {
        return stage switch
        {
            FishingStage.CastWindup => Math.Clamp(stageTicks * 6 / Math.Max(1, CastWindupTicks), 0, 5),
            FishingStage.CastRelease => Math.Clamp(5 - stageTicks * 6 / Math.Max(1, CastReleaseTicks), 0, 5),
            FishingStage.BobberLanding => 4,
            FishingStage.WaitingForBite => 4,
            FishingStage.BiteReaction => Math.Clamp(4 + stageTicks * 2 / Math.Max(1, BiteReactionTicks), 4, 5),
            FishingStage.ReelWindup => 1,
            FishingStage.Reeling => Math.Clamp(stageTicks * 6 / Math.Max(1, ReelAnimationTicks), 0, 5),
            _ => 0,
        };
    }

    private void DrawFishingLine(SpriteBatch spriteBatch, Vector2 rodTip, Vector2 bobber)
    {
        Vector2 midpoint = Vector2.Lerp(rodTip, bobber, 0.5f);
        float sag = stage is FishingStage.WaitingForBite or FishingStage.BobberLanding
            ? 7f
            : stage is FishingStage.BiteReaction or FishingStage.ReelWindup or FishingStage.Reeling
                ? -5f
                : 0f;
        midpoint.Y += sag;
        DrawLine(spriteBatch, rodTip, midpoint, Color.White * 0.84f, 1.25f);
        DrawLine(spriteBatch, midpoint, bobber, Color.White * 0.84f, 1.25f);
    }

    private void DrawBobber(SpriteBatch spriteBatch, Vector2 bobber)
    {
        Rectangle source = new(0, 0, 16, 16);
        spriteBatch.Draw(
            Game1.bobbersTexture,
            bobber - new Vector2(8f, 8f),
            source,
            stage == FishingStage.BiteReaction ? Color.OrangeRed : Color.White,
            0f,
            Vector2.Zero,
            1.5f,
            SpriteEffects.None,
            0.997f);
        if (stage is FishingStage.BiteReaction or FishingStage.ReelWindup)
        {
            float pulse = 8f + MathF.Sin(stageTicks * 0.8f) * 3f;
            DrawLine(
                spriteBatch,
                bobber - new Vector2(pulse, 0f),
                bobber + new Vector2(pulse, 0f),
                Color.White * 0.55f,
                1f);
        }
    }

    private Vector2 GetBobberScreenPosition(Vector2 viewport, FishingPose pose)
    {
        if (fishingSpot is null)
            return pose.Hand;
        Vector2 water = new Vector2(
            (fishingSpot.WaterTile.X + 0.5f) * Game1.tileSize,
            (fishingSpot.WaterTile.Y + 0.5f) * Game1.tileSize) - viewport;
        Vector2 rodTip = pose.RodTip;
        if (stage == FishingStage.CastWindup)
        {
            float progress = Math.Clamp(stageTicks / (float)CastWindupTicks, 0f, 1f);
            return Vector2.Lerp(pose.Hand, rodTip, progress);
        }
        if (stage is FishingStage.CastRelease or FishingStage.BobberLanding)
        {
            float progress = stage == FishingStage.CastRelease
                ? Math.Clamp(stageTicks / (float)CastReleaseTicks, 0f, 1f)
                : 1f;
            Vector2 cast = Vector2.Lerp(rodTip, water, progress);
            cast.Y -= MathF.Sin(progress * MathF.PI) * 72f;
            if (stage == FishingStage.BobberLanding)
                cast.Y += MathF.Sin(stageTicks / (float)BobberLandingTicks * MathF.PI) * 8f;
            return cast;
        }
        if (stage == FishingStage.BiteReaction)
        {
            return water + new Vector2(
                0f,
                8f + MathF.Sin(stageTicks / (float)BiteReactionTicks * MathF.PI) * 5f);
        }
        if (stage == FishingStage.ReelWindup)
            return water;
        if (stage == FishingStage.Reeling)
        {
            float progress = Math.Clamp(stageTicks / (float)ReelAnimationTicks, 0f, 1f);
            Vector2 reel = Vector2.Lerp(water, pose.Hand, progress);
            reel.Y -= MathF.Sin(progress * MathF.PI) * 54f;
            return reel;
        }
        return water + new Vector2(0f, MathF.Sin(stageTicks / 9f) * 2f);
    }

    private readonly record struct FishingPose(
        Vector2 Hand,
        Vector2 RodTip,
        Vector2 ToolPosition,
        int Frame,
        SpriteEffects Effects,
        Rectangle Source);

    private void AbortFishingAction(string reason)
    {
        if (stage != FishingStage.Ready)
        {
            monitor.Log($"invite_fishing_companion cast_cancelled npc={npc.Name} reason={reason}.", LogLevel.Debug);
        }
        StopNavigation();
        stage = FishingStage.Ready;
        stageTicks = 0;
        fishingSpot = null;
        caughtItem = null;
        pendingPlayerCast = false;
    }

    private void Finish(string reason)
    {
        if (complete)
            return;
        AbortFishingAction(reason);
        scheduleRecovery.Release(npc, originalIgnoreScheduleToday, "invite_fishing_companion");
        npc.speed = originalSpeed;
        complete = true;
        monitor.Log($"invite_fishing_companion session_ended npc={npc.Name} reason={reason}.", LogLevel.Info);
    }

    private void StopNavigation()
    {
        navigation.Stop(npc);
        navigating = false;
        npc.speed = originalSpeed;
    }

    private bool IsPlayerFishing()
        => leader.CurrentTool is FishingRod rod && (rod.isFishing || rod.inUse());

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

    private static double TileDistance(Point first, Point second)
        => Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    private static Vector2 FacingVector(int facingDirection)
        => facingDirection switch
        {
            0 => new Vector2(0f, -1f),
            1 => new Vector2(1f, 0f),
            2 => new Vector2(0f, 1f),
            _ => new Vector2(-1f, 0f),
        };

    private static void DrawLine(
        SpriteBatch spriteBatch,
        Vector2 start,
        Vector2 end,
        Color color,
        float width)
    {
        Vector2 delta = end - start;
        float length = delta.Length();
        if (length < 0.5f)
            return;
        spriteBatch.Draw(
            Game1.staminaRect,
            start,
            null,
            color,
            MathF.Atan2(delta.Y, delta.X),
            Vector2.Zero,
            new Vector2(length, width),
            SpriteEffects.None,
            0.999f);
    }

    private enum FishingStage
    {
        Ready,
        MovingToSpot,
        CastWindup,
        CastRelease,
        BobberLanding,
        WaitingForBite,
        BiteReaction,
        ReelWindup,
        Reeling,
        HoldingCatch,
    }

    private sealed record FishingSpot(
        Point StandingTile,
        Point WaterTile,
        int FacingDirection,
        int WaterDepth,
        IReadOnlyList<Point> Path);
}
