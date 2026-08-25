using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Tools;

namespace VivantValley.Services;

/// <summary>Owns real NPC mine-guard sessions and their main-thread combat.</summary>
public sealed class NpcMineGuardService
{
    private readonly IMonitor monitor;
    private readonly NpcCombatStateService combatState;
    private readonly NpcTilePathfinder pathfinder = new();
    private readonly NpcScheduleRecoveryService scheduleRecovery;
    private readonly Dictionary<string, NpcMineGuardSession> sessions = new(StringComparer.Ordinal);

    public NpcMineGuardService(IMonitor monitor, NpcCombatStateService combatState)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        this.combatState = combatState ?? throw new ArgumentNullException(nameof(combatState));
        scheduleRecovery = new NpcScheduleRecoveryService(this.monitor);
    }

    public bool CanInvite(NPC npc, GameLocation? playerLocation)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return GetAvailabilityReason(npc, playerLocation) is null;
    }

    public bool HasActiveSession(string? npcName)
        => !string.IsNullOrWhiteSpace(npcName) && sessions.ContainsKey(npcName);

    public string? GetActivitySummary(string? npcName)
        => !string.IsNullOrWhiteSpace(npcName) && sessions.ContainsKey(npcName)
            ? "正在陪玩家下矿并担任护卫，留意附近怪物"
            : null;

    /// <summary>
    /// Returns the authoritative reason why the mine-guard tool is unavailable.
    /// A null result means the game can start the session if the NPC chooses to accept.
    /// </summary>
    public string? GetAvailabilityReason(NPC npc, GameLocation? playerLocation)
    {
        ArgumentNullException.ThrowIfNull(npc);
        return CanStart(npc, Game1.player, playerLocation, out string reason)
            ? null
            : reason;
    }

    public ConversationMineGuardExecutionResult Execute(NPC npc, Farmer leader)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(leader);
        if (!CanStart(npc, leader, leader.currentLocation, out string reason))
        {
            return new ConversationMineGuardExecutionResult
            {
                RequestedToolName = NpcMineGuardToolNames.InviteMineGuard,
                Outcome = ConversationMineGuardOutcome.Rejected,
                FailureReason = reason,
            };
        }

        try
        {
            if (sessions.TryGetValue(npc.Name, out NpcMineGuardSession? existingSession))
            {
                existingSession.Cancel("replaced_by_new_mine_guard");
                sessions.Remove(npc.Name);
            }

            sessions[npc.Name] = new NpcMineGuardSession(
                npc,
                leader,
                pathfinder,
                monitor,
                combatState,
                scheduleRecovery);
            monitor.Log(
                $"invite_mine_guard session_started npc={npc.Name} location={leader.currentLocation.NameOrUniqueName}.",
                LogLevel.Info);
            return new ConversationMineGuardExecutionResult
            {
                RequestedToolName = NpcMineGuardToolNames.InviteMineGuard,
                Outcome = ConversationMineGuardOutcome.Guarding,
            };
        }
        catch (Exception exception)
        {
            return new ConversationMineGuardExecutionResult
            {
                RequestedToolName = NpcMineGuardToolNames.InviteMineGuard,
                Outcome = ConversationMineGuardOutcome.Failed,
                FailureReason = CleanReason(exception.Message, "mine_guard_start_failed"),
            };
        }
    }

    public void Update()
    {
        foreach ((string npcName, NpcMineGuardSession session) in sessions.ToArray())
        {
            session.Update();
            if (session.IsComplete)
                sessions.Remove(npcName);
        }
    }

    public void CancelAll(string reason)
    {
        foreach (NpcMineGuardSession session in sessions.Values)
            session.Cancel(reason);
        sessions.Clear();
    }

    public bool CancelNpc(string? npcName, string reason)
    {
        if (string.IsNullOrWhiteSpace(npcName)
            || !sessions.TryGetValue(npcName, out NpcMineGuardSession? session))
        {
            return false;
        }

        session.Cancel(reason);
        sessions.Remove(npcName);
        return true;
    }

    public void DrawWorld(SpriteBatch spriteBatch)
    {
        foreach (NpcMineGuardSession session in sessions.Values)
            session.DrawWorld(spriteBatch);
    }

    private bool CanStart(NPC npc, Farmer? leader, GameLocation? playerLocation, out string reason)
    {
        reason = string.Empty;
        if (!Game1.IsMasterGame)
            reason = "host_required";
        else if (leader is null || playerLocation is null || npc.currentLocation is null
                 || !ReferenceEquals(leader.currentLocation, playerLocation)
                 || !ReferenceEquals(npc.currentLocation, playerLocation))
            reason = "npc_not_with_player";
        else if (!npc.IsVillager || npc.IsMonster || npc.IsInvisible || !npc.CanSocialize)
            reason = "npc_unavailable";
        else if (!combatState.HasUsableWeapon(npc.Name))
            reason = "default_weapon_unavailable";
        else if (Game1.eventUp || Game1.isFestival() || playerLocation.currentEvent is not null)
            reason = "event_active";
        else if (Game1.timeOfDay is < 600 or > 2300)
            reason = "time_not_allowed";
        return reason.Length == 0;
    }

    internal static bool IsMineLocation(GameLocation? location)
        => location is MineShaft
           || location is Mine
           || (location?.NameOrUniqueName?.Contains("Mine", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string CleanReason(string? value, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length == 0 ? fallback : clean.Length <= 160 ? clean : clean[..160];
    }
}

internal sealed class NpcMineGuardSession
{
    // Let the player clear a doorway, warp tile, or mine entrance before the NPC follows.
    private const int CrossMapTransferDelayTicks = 90;
    private const int PathRetryTicks = 20;
    private const int MinimumFollowDistance = 1;
    private const int MaximumFollowDistance = 2;
    private const int CombatSearchRadius = 8;
    // Keep an engaged monster locked through a short knockback.
    private const int CombatTargetRetentionRadius = 12;
    private const int MaximumCombatDistanceFromPlayer = 8;
    // Replanning on every small player/monster movement makes the NPC repeatedly
    // stop and restart its controller. Keep the current approach until the target
    // actually changes position or the route has had time to make progress.
    private const int CombatReplanIntervalTicks = 15;
    private const int CombatRetryDelayTicks = 3;
    private const int HealthBarWidth = 72;
    private const int HealthBarHeight = 6;
    private const int HealthBarGap = 4;
    // The vanilla sword animation has six visible frames. NPCs cannot use
    // MeleeWeapon.setFarmerAnimating directly because that API requires a Farmer
    // and FarmerSprite, so the session drives the same weapon frames itself.
    private const int AttackFrameCount = 6;
    private const int AttackFrameTicks = 3;
    private const int AttackAnimationTicks = AttackFrameCount * AttackFrameTicks;
    private const int AttackImpactFrame = 3;

    private readonly NPC npc;
    private readonly Farmer leader;
    private readonly NpcTilePathfinder pathfinder;
    private readonly NpcNavigationController navigation = new();
    private readonly NpcNavigationController combatNavigation = new();
    private readonly IMonitor monitor;
    private readonly NpcCombatStateService combatState;
    private readonly NpcScheduleRecoveryService scheduleRecovery;
    private readonly NpcWeaponSnapshot weapon;
    private readonly MeleeWeapon? weaponPreview;
    private readonly bool originalIgnoreScheduleToday;
    private readonly int originalSpeed;
    private int differentLocationTicks;
    private int attackCooldownTicks;
    private int incomingDamageCooldownTicks;
    private int pathRetryTicks;
    private int combatRetryTicks;
    private int attackAnimationTicks;
    private int attackFacingDirection;
    private int combatReplanTicks;
    private Monster? attackTarget;
    private Monster? combatTarget;
    private Point plannedCombatTargetTile;
    private bool attackImpactApplied;
    private bool navigating;
    private bool combatNavigating;
    private bool enteredMine;
    private bool complete;

    public NpcMineGuardSession(
        NPC npc,
        Farmer leader,
        NpcTilePathfinder pathfinder,
        IMonitor monitor,
        NpcCombatStateService combatState,
        NpcScheduleRecoveryService scheduleRecovery)
    {
        this.npc = npc;
        this.leader = leader;
        this.pathfinder = pathfinder;
        this.monitor = monitor;
        this.combatState = combatState;
        this.scheduleRecovery = scheduleRecovery;
        weapon = combatState.GetWeapon(npc.Name)
                 ?? throw new InvalidOperationException("default_weapon_unavailable");
        weaponPreview = combatState.CreateWeaponItem(npc.Name);
        originalIgnoreScheduleToday = npc.ignoreScheduleToday;
        originalSpeed = npc.speed;
        NpcCombatState state = combatState.GetOrCreate(npc.Name);
        npc.ignoreScheduleToday = true;
    }

    public bool IsComplete => complete;

    public void Update()
    {
        if (complete || !Context.IsWorldReady)
            return;
        if ((Game1.activeClickableMenu is not null && !Game1.IsMultiplayer) || Game1.dialogueUp)
            return;
        if (leader.currentLocation is null || npc.currentLocation is null
            || Game1.eventUp || Game1.isFestival()
            || npc.IsInvisible || !npc.CanSocialize)
        {
            Finish("ended_npc_unavailable");
            return;
        }
        if (combatState.IsHospitalized(npc.Name))
        {
            Finish("npc_defeated");
            return;
        }

        bool playerInMine = NpcMineGuardService.IsMineLocation(leader.currentLocation);
        if (enteredMine && !playerInMine)
        {
            Finish("ended_player_left_mine");
            return;
        }
        if (playerInMine)
            enteredMine = true;

        if (!ReferenceEquals(npc.currentLocation, leader.currentLocation))
        {
            UpdateCrossMapTransfer();
            return;
        }

        differentLocationTicks = 0;
        if (playerInMine)
        {
            if (UpdateIncomingDamage(leader.currentLocation))
                return;
            if (!UpdateCombat(leader.currentLocation))
                UpdateFollow();
        }
        else
        {
            UpdateFollow();
        }
    }

    public void Cancel(string reason)
    {
        if (complete)
            return;
        Finish(reason);
    }

    public void DrawWorld(SpriteBatch spriteBatch)
    {
        if (complete
            || !Context.IsWorldReady
            || npc.currentLocation is null
            || !ReferenceEquals(npc.currentLocation, Game1.currentLocation)
            || !NpcMineGuardService.IsMineLocation(npc.currentLocation))
        {
            return;
        }

        NpcCombatState state = combatState.GetOrCreate(npc.Name);
        float ratio = state.MaxHealth <= 0 ? 0f : Math.Clamp(state.CurrentHealth / (float)state.MaxHealth, 0f, 1f);
        Rectangle worldBounds = npc.GetBoundingBox();
        int barLeft = worldBounds.Center.X - (HealthBarWidth / 2);
        int barTop = worldBounds.Top - HealthBarHeight - HealthBarGap;
        Rectangle barBackground = new(
            barLeft - Game1.viewport.X,
            barTop - Game1.viewport.Y,
            HealthBarWidth,
            HealthBarHeight);
        Rectangle bar = new(barBackground.X + 1, barBackground.Y + 1, Math.Max(0, (int)((barBackground.Width - 2) * ratio)), barBackground.Height - 2);
        spriteBatch.Draw(Game1.staminaRect, barBackground, Color.Black * 0.85f);
        Color healthColor = ratio > 0.6f ? Color.LimeGreen : ratio > 0.3f ? Color.Gold : Color.Red;
        if (bar.Width > 0)
            spriteBatch.Draw(Game1.staminaRect, bar, healthColor);

        DrawAttackWeapon(spriteBatch);
    }

    /// <summary>Draw the original weapon sprite using the vanilla sword swing poses.</summary>
    private void DrawAttackWeapon(SpriteBatch spriteBatch)
    {
        if (attackAnimationTicks <= 0
            || weaponPreview is null)
        {
            return;
        }

        try
        {
            var data = ItemRegistry.GetDataOrErrorItem(weaponPreview.GetDrawnItemId());
            Texture2D texture = data.GetTexture() ?? Tool.weaponsTexture;
            Rectangle sourceRect = data.GetSourceRect();
            int frame = GetAttackFrame();
            GetVanillaSwordPose(
                attackFacingDirection,
                frame,
                out Vector2 offset,
                out float rotation,
                out SpriteEffects effects);
            Vector2 position = npc.Position
                               - new Vector2(Game1.viewport.X, Game1.viewport.Y)
                               + offset;
            spriteBatch.Draw(
                texture,
                position,
                sourceRect,
                Color.White,
                rotation,
                new Vector2(1f, 15f),
                4f,
                effects,
                0.999f);
        }
        catch (Exception exception)
        {
            monitor.Log(
                $"invite_mine_guard weapon_draw_failed npc={npc.Name} reason={exception.Message}.",
                LogLevel.Debug);
        }
    }

    private int GetAttackFrame()
    {
        int elapsed = AttackAnimationTicks - attackAnimationTicks;
        return Math.Clamp(elapsed / AttackFrameTicks, 0, AttackFrameCount - 1);
    }

    /// <summary>
    /// Positions and rotates the weapon exactly like the normal sword branch in
    /// MeleeWeapon.drawDuringUse. The NPC uses the same item texture, but does not
    /// borrow the player's FarmerSprite or mutate the player's facing direction.
    /// </summary>
    private static void GetVanillaSwordPose(
        int facingDirection,
        int frame,
        out Vector2 offset,
        out float rotation,
        out SpriteEffects effects)
    {
        frame = Math.Clamp(frame, 0, AttackFrameCount - 1);
        effects = SpriteEffects.None;
        switch (facingDirection)
        {
            case 1:
                (offset, rotation) = frame switch
                {
                    0 => (new Vector2(40f, -56f), -MathF.PI / 4f),
                    1 => (new Vector2(56f, -36f), 0f),
                    2 => (new Vector2(60f, -16f), MathF.PI / 4f),
                    3 => (new Vector2(60f, -4f), MathF.PI / 2f),
                    4 => (new Vector2(36f, 4f), MathF.PI * 5f / 8f),
                    _ => (new Vector2(16f, 4f), MathF.PI * 3f / 4f),
                };
                break;
            case 3:
                effects = SpriteEffects.FlipHorizontally;
                (offset, rotation) = frame switch
                {
                    0 => (new Vector2(-16f, -80f), MathF.PI / 4f),
                    1 => (new Vector2(-48f, -44f), 0f),
                    2 => (new Vector2(-32f, 16f), -MathF.PI / 4f),
                    3 => (new Vector2(4f, 44f), -MathF.PI / 2f),
                    4 => (new Vector2(44f, 52f), -MathF.PI * 5f / 8f),
                    _ => (new Vector2(80f, 40f), -MathF.PI * 3f / 4f),
                };
                break;
            case 0:
                (offset, rotation) = frame switch
                {
                    0 => (new Vector2(32f, -32f), -MathF.PI * 3f / 4f),
                    1 => (new Vector2(32f, -48f), -MathF.PI / 2f),
                    2 => (new Vector2(48f, -52f), -MathF.PI * 3f / 8f),
                    3 => (new Vector2(48f, -52f), -MathF.PI / 8f),
                    4 => (new Vector2(56f, -40f), 0f),
                    _ => (new Vector2(64f, -40f), MathF.PI / 8f),
                };
                break;
            default:
                (offset, rotation) = frame switch
                {
                    0 => (new Vector2(56f, -16f), MathF.PI / 8f),
                    1 => (new Vector2(52f, -8f), MathF.PI / 2f),
                    2 => (new Vector2(40f, 0f), MathF.PI / 2f),
                    3 => (new Vector2(16f, 4f), MathF.PI * 3f / 4f),
                    4 => (new Vector2(8f, 8f), MathF.PI),
                    _ => (new Vector2(12f, 0f), 3.5342917f),
                };
                break;
        }
    }

    private void UpdateCrossMapTransfer()
    {
        StopNavigation();
        ClearCombatTarget("map_transfer");
        differentLocationTicks++;
        if (differentLocationTicks < CrossMapTransferDelayTicks)
            return;

        if (leader.currentLocation.currentEvent is not null
            || !pathfinder.TryFindSafeFollowTile(
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
        pathRetryTicks = 0;
        combatRetryTicks = 0;
        monitor.Log(
            $"invite_mine_guard follower_transferred npc={npc.Name} source={source.NameOrUniqueName} "
            + $"target={leader.currentLocation.NameOrUniqueName} tile={arrivalTile.X},{arrivalTile.Y}.",
            LogLevel.Debug);
    }

    private void UpdateFollow()
    {
        if (combatNavigating)
            return;
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
                pathRetryTicks = PathRetryTicks;
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
            pathRetryTicks = PathRetryTicks;
            return;
        }

        npc.speed = separation >= 5 ? 7 : 6;
        navigation.Start(path, npc);
        navigating = true;
    }

    private bool UpdateCombat(GameLocation location)
    {
        if (attackCooldownTicks > 0)
            attackCooldownTicks--;

        if (attackAnimationTicks > 0)
        {
            UpdateAttackAnimation(location);
            return true;
        }

        if (ManhattanDistance(npc.TilePoint, leader.TilePoint) > MaximumCombatDistanceFromPlayer)
        {
            ClearCombatTarget("player_leash_exceeded");
            return false;
        }

        // Acquire around the guard, then constrain every approach path to the player's
        // eight-tile leash so an engaged monster cannot pull the NPC across the floor.
        // Retain the current target through a short knockback so it is not dropped
        // between two update ticks merely because it left the acquisition ring.
        List<Monster> targets = location.characters
            .OfType<Monster>()
            .Where(monster => IsValidCombatTarget(monster, location))
            .Where(monster => ReferenceEquals(monster, combatTarget)
                              ? ManhattanDistance(monster.TilePoint, npc.TilePoint) <= CombatTargetRetentionRadius
                              : ManhattanDistance(monster.TilePoint, npc.TilePoint) <= CombatSearchRadius)
            .OrderBy(monster => ReferenceEquals(monster, combatTarget) ? 0 : 1)
            .ThenBy(monster => ManhattanDistance(monster.TilePoint, npc.TilePoint))
            .ThenByDescending(monster => monster.DamageToFarmer)
            .ThenBy(monster => ManhattanDistance(monster.TilePoint, leader.TilePoint))
            .ToList();
        if (targets.Count == 0)
        {
            ClearCombatTarget("no_target_in_range");
            combatRetryTicks = 0;
            return false;
        }

        if (combatNavigating)
        {
            Monster? target = combatTarget;
            combatReplanTicks++;
            if (target is not null && IsInMeleeRange(target))
            {
                StopCombatNavigation();
                if (attackCooldownTicks <= 0)
                    BeginAttack(target);
                return true;
            }

            bool mustReplan = target is null
                              || !targets.Contains(target)
                              || ManhattanDistance(target.TilePoint, plannedCombatTargetTile) > 1
                              || combatReplanTicks >= CombatReplanIntervalTicks;
            if (!mustReplan)
            {
                npc.speed = 8;
                NpcNavigationStatus status = combatNavigation.Update(npc);
                if (status == NpcNavigationStatus.Moving)
                    return true;
            }

            StopCombatNavigation();
        }

        if (combatRetryTicks > 0)
        {
            combatRetryTicks--;
            return false;
        }

        foreach (Monster target in targets)
        {
            SetCombatTarget(target);
            if (IsInMeleeRange(target))
            {
                StopCombatNavigation();
                if (attackCooldownTicks > 0)
                    return true;

                BeginAttack(target);
                return true;
            }

            if (pathfinder.TryFindPathToAdjacent(
                    location,
                    npc,
                    npc.TilePoint,
                    target.TilePoint,
                    leader.TilePoint,
                    MaximumCombatDistanceFromPlayer,
                    out IReadOnlyList<Point> path,
                    out Point standingTile)
                && path.Count > 1)
            {
                StopNavigation();
                npc.speed = 8;
                combatNavigation.Start(path, npc);
                combatNavigating = true;
                plannedCombatTargetTile = target.TilePoint;
                combatReplanTicks = 0;
                combatRetryTicks = 0;
                monitor.Log(
                    $"invite_mine_guard approach_started npc={npc.Name} target={target.Name} "
                    + $"standing={standingTile.X},{standingTile.Y} length={path.Count}.",
                    LogLevel.Debug);
                return true;
            }
        }

        // A target may be behind a temporary monster or rock. Follow the player during
        // the short retry window instead of freezing in place.
        ClearCombatTarget("no_reachable_adjacent_tile");
        combatRetryTicks = CombatRetryDelayTicks;
        return false;
    }

    private void BeginAttack(Monster target)
    {
        SetCombatTarget(target);
        attackTarget = target;
        attackAnimationTicks = AttackAnimationTicks;
        attackImpactApplied = false;
        attackFacingDirection = FacingDirection(npc.TilePoint, target.TilePoint);
        npc.faceDirection(attackFacingDirection);
        npc.Halt();
        monitor.Log(
            $"invite_mine_guard attack_started npc={npc.Name} target={target.Name}.",
            LogLevel.Debug);
    }

    private void UpdateAttackAnimation(GameLocation location)
    {
        Monster? target = attackTarget;
        if (target is null
            || !IsValidCombatTarget(target, location))
        {
            StopAttackAnimation();
            return;
        }

        int frame = GetAttackFrame();
        attackAnimationTicks--;
        if (!attackImpactApplied && frame >= AttackImpactFrame)
        {
            attackImpactApplied = true;
            ApplyAttackDamage(location, target);
        }

        if (attackAnimationTicks <= 0)
        {
            StopAttackAnimation();
            attackCooldownTicks = GetAttackCooldownTicks();
        }
    }

    private void ApplyAttackDamage(GameLocation location, Monster target)
    {
        Rectangle area = GetAttackArea(attackFacingDirection, AttackImpactFrame);
        if (!area.Intersects(target.GetBoundingBox()))
            return;

        int upgradeBonus = Math.Max(0, weapon.UpgradeLevel * 2);
        int minimumDamage = Math.Max(1, weapon.MinDamage + upgradeBonus);
        int maximumDamage = Math.Max(minimumDamage, weapon.MaxDamage + upgradeBonus);
        bool hit = location.damageMonster(
            area,
            minimumDamage,
            maximumDamage,
            false,
            weapon.Knockback,
            0,
            weapon.CritChance,
            weapon.CritMultiplier,
            true,
            leader,
            true);
        if (hit && target.Health > 0 && target.Slipperiness != -1)
        {
            // GameLocation.damageMonster calculates trajectory from the Farmer
            // argument. The NPC is the actual attacker, so correct that vector
            // after the authoritative hit using the NPC's position.
            Vector2 trajectory = Utility.getAwayFromPositionTrajectory(
                target.GetBoundingBox(),
                npc.GetBoundingBox().Center.ToVector2());
            float knockback = Math.Max(1.2f, weapon.Knockback);
            target.setTrajectory(trajectory * knockback);
            if (target.stunTime.Value < 30)
                target.stunTime.Value = 30;
        }
        monitor.Log(
            $"invite_mine_guard attack_impact npc={npc.Name} target={target.Name} hit={hit} "
            + $"remaining_health={target.Health}.",
            LogLevel.Debug);
    }

    private void StopAttackAnimation()
    {
        attackAnimationTicks = 0;
        attackTarget = null;
        attackImpactApplied = false;
    }

    private void SetCombatTarget(Monster target)
    {
        if (ReferenceEquals(combatTarget, target))
            return;

        combatTarget = target;
        plannedCombatTargetTile = target.TilePoint;
        combatReplanTicks = 0;
        monitor.Log(
            $"invite_mine_guard target_acquired npc={npc.Name} target={target.Name} "
            + $"npc_distance={ManhattanDistance(npc.TilePoint, target.TilePoint)}.",
            LogLevel.Debug);
    }

    private void ClearCombatTarget(string reason)
    {
        Monster? previous = combatTarget;
        StopCombatNavigation();
        StopAttackAnimation();
        combatTarget = null;
        plannedCombatTargetTile = Point.Zero;
        combatReplanTicks = 0;
        if (previous is not null)
        {
            monitor.Log(
                $"invite_mine_guard target_released npc={npc.Name} target={previous.Name} reason={reason}.",
                LogLevel.Debug);
        }
    }

    private bool IsInMeleeRange(Monster target)
        => GetAttackArea(
                FacingDirection(npc.TilePoint, target.TilePoint),
                AttackImpactFrame)
            .Intersects(target.GetBoundingBox());

    private Rectangle GetAttackArea(int facingDirection, int frame)
    {
        if (weaponPreview is null)
        {
            Rectangle fallback = npc.GetBoundingBox();
            fallback.Inflate(Game1.tileSize / 2, Game1.tileSize / 2);
            return fallback;
        }

        Vector2 toolLocation = npc.GetBoundingBox().Center.ToVector2()
                               + FacingVector(facingDirection) * Game1.tileSize;
        Vector2 tileLocation1 = Vector2.Zero;
        Vector2 tileLocation2 = Vector2.Zero;
        return weaponPreview.getAreaOfEffect(
            (int)toolLocation.X,
            (int)toolLocation.Y,
            facingDirection,
            ref tileLocation1,
            ref tileLocation2,
            npc.GetBoundingBox(),
            Math.Clamp(frame, 0, AttackFrameCount - 1));
    }

    private static bool IsValidCombatTarget(Monster monster, GameLocation location)
        => monster.Health > 0
           && !monster.IsInvisible
           && ReferenceEquals(monster.currentLocation, location);

    private bool UpdateIncomingDamage(GameLocation location)
    {
        if (incomingDamageCooldownTicks > 0)
        {
            incomingDamageCooldownTicks--;
            return false;
        }

        Rectangle npcBounds = npc.GetBoundingBox();
        npcBounds.Inflate(Game1.tileSize / 4, Game1.tileSize / 4);
        Monster? attacker = location.characters
            .OfType<Monster>()
            .Where(monster => monster.Health > 0 && !monster.IsInvisible)
            .Where(monster => monster.GetBoundingBox().Intersects(npcBounds))
            .OrderBy(monster => TileDistance(monster.TilePoint, npc.TilePoint))
            .FirstOrDefault();
        if (attacker is null)
            return false;

        incomingDamageCooldownTicks = 45;
        int damage = Math.Max(1, attacker.DamageToFarmer);
        bool defeated = combatState.ApplyDamage(npc, leader, damage, attacker.Name);
        npc.showTextAboveHead(defeated ? "我得去看医生了……" : $"-{damage}");
        monitor.Log(
            $"invite_mine_guard npc_damage npc={npc.Name} source={attacker.Name} damage={damage} "
            + $"remaining_health={combatState.GetOrCreate(npc.Name).CurrentHealth}.",
            LogLevel.Debug);
        if (!defeated)
            return true;

        StopNavigation();
        StopCombatNavigation();
        Finish("npc_defeated");
        return true;
    }

    private int GetAttackCooldownTicks()
        => Math.Clamp(8 - weapon.Speed, 3, 8);

    private void Finish(string reason)
    {
        if (complete)
            return;
        StopNavigation();
        ClearCombatTarget(reason);
        if (!combatState.IsHospitalized(npc.Name))
        {
            if (reason.Equals("ended_player_left_mine", StringComparison.Ordinal))
            {
                scheduleRecovery.Release(npc, originalIgnoreScheduleToday, "invite_mine_guard");
            }
            else
            {
                npc.ignoreScheduleToday = originalIgnoreScheduleToday;
            }
        }
        complete = true;
        monitor.Log($"invite_mine_guard session_ended npc={npc.Name} reason={reason}.", LogLevel.Info);
    }

    private void StopNavigation()
    {
        navigation.Stop(npc);
        navigating = false;
        npc.speed = originalSpeed;
    }

    private void StopCombatNavigation()
    {
        combatNavigation.Stop(npc);
        combatNavigating = false;
        npc.speed = originalSpeed;
    }

    private static double TileDistance(Point first, Point second)
        => Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));

    private static int ManhattanDistance(Point first, Point second)
        => Math.Abs(first.X - second.X) + Math.Abs(first.Y - second.Y);

    private static int FacingDirection(Point standing, Point target)
    {
        Point delta = target - standing;
        if (Math.Abs(delta.X) > Math.Abs(delta.Y))
            return delta.X >= 0 ? 1 : 3;
        return delta.Y >= 0 ? 2 : 0;
    }

    private static Vector2 FacingVector(int facingDirection)
        => facingDirection switch
        {
            0 => new Vector2(0f, -1f),
            1 => new Vector2(1f, 0f),
            2 => new Vector2(0f, 1f),
            _ => new Vector2(-1f, 0f),
        };
}
