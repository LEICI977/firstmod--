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
        else if (sessions.ContainsKey(npc.Name) || npc.controller is not null || npc.temporaryController is not null)
            reason = "npc_busy";
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
    private const int PlayerCombatSearchRadius = 8;
    private const int HealthBarWidth = 72;
    private const int HealthBarHeight = 6;
    private const int HealthBarGap = 4;
    // Keep the attack readable while avoiding a long stationary window in which
    // the NPC can be hit repeatedly by a monster.
    private const int AttackAnimationTicks = 6;
    private const int AttackImpactTick = 3;

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
    private Monster? attackTarget;
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

        DrawAttackEffect(spriteBatch);

        if (weaponPreview is null)
            return;

        Vector2 screen = npc.Position - new Vector2(Game1.viewport.X, Game1.viewport.Y);
        try
        {
            weaponPreview.drawInMenu(
                spriteBatch,
                new Vector2(screen.X + 20, screen.Y - 52),
                0.42f,
                1f,
                0f,
                StackDrawType.Hide,
                Color.White,
                false);
        }
        catch
        {
            // A custom weapon may not have a menu sprite; the combat still works.
        }
    }

    private void DrawAttackEffect(SpriteBatch spriteBatch)
    {
        if (attackAnimationTicks <= 0
            || attackTarget is null
            || attackTarget.currentLocation is null
            || !ReferenceEquals(attackTarget.currentLocation, npc.currentLocation))
        {
            return;
        }

        Vector2 start = npc.GetBoundingBox().Center.ToVector2()
                        - new Vector2(Game1.viewport.X, Game1.viewport.Y);
        Vector2 target = attackTarget.GetBoundingBox().Center.ToVector2()
                         - new Vector2(Game1.viewport.X, Game1.viewport.Y);
        Vector2 direction = target - start;
        if (direction.LengthSquared() < 1f)
            direction = FacingVector(npc.FacingDirection);
        else
            direction.Normalize();

        Vector2 perpendicular = new(-direction.Y, direction.X);
        float progress = 1f - attackAnimationTicks / (float)AttackAnimationTicks;
        float sweep = MathF.Sin(progress * MathF.PI) * 18f;
        Vector2 slashStart = start + direction * 10f + perpendicular * sweep;
        Vector2 slashEnd = slashStart + direction * 48f - perpendicular * (sweep * 1.8f);
        Vector2 delta = slashEnd - slashStart;
        float length = delta.Length();
        if (length < 1f)
            return;

        spriteBatch.Draw(
            Game1.staminaRect,
            slashStart,
            null,
            Color.Gold * 0.9f,
            MathF.Atan2(delta.Y, delta.X),
            Vector2.Zero,
            new Vector2(length, 4f),
            SpriteEffects.None,
            0.999f);
    }

    private void UpdateCrossMapTransfer()
    {
        StopNavigation();
        StopCombatNavigation();
        StopAttackAnimation();
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
        if (attackAnimationTicks > 0)
        {
            UpdateAttackAnimation(location);
            return true;
        }

        // Mine guard duty protects the player; it never sends the NPC hunting across
        // the floor. Only monsters within eight tiles of the player are candidates.
        List<Monster> targets = location.characters
            .OfType<Monster>()
            .Where(monster => monster.Health > 0 && !monster.IsInvisible)
            .Where(monster => TileDistance(monster.TilePoint, leader.TilePoint) <= PlayerCombatSearchRadius)
            .OrderBy(monster => TileDistance(monster.TilePoint, leader.TilePoint))
            .ToList();
        if (targets.Count == 0)
        {
            StopCombatNavigation();
            combatRetryTicks = 0;
            return false;
        }

        if (combatNavigating)
        {
            npc.speed = 7;
            NpcNavigationStatus status = combatNavigation.Update(npc);
            if (status == NpcNavigationStatus.Moving)
                return true;
            StopCombatNavigation();
        }

        if (combatRetryTicks > 0)
        {
            combatRetryTicks--;
            return false;
        }

        foreach (Monster target in targets)
        {
            double distance = TileDistance(target.TilePoint, npc.TilePoint);
            if (distance <= 2)
            {
                StopCombatNavigation();
                if (attackCooldownTicks > 0)
                {
                    attackCooldownTicks--;
                    return true;
                }

                BeginAttack(target);
                return true;
            }

            if (pathfinder.TryFindPathToFollowRange(
                    location,
                    npc,
                    npc.TilePoint,
                    target.TilePoint,
                    MinimumFollowDistance,
                    MinimumFollowDistance,
                    out IReadOnlyList<Point> path)
                && path.Count > 1)
            {
                StopNavigation();
                npc.speed = 7;
                combatNavigation.Start(path, npc);
                combatNavigating = true;
                combatRetryTicks = 0;
                return true;
            }
        }

        // A nearby target can still be behind a temporary obstacle. Keep following the
        // player and retry the whole eight-tile scan instead of standing still.
        combatRetryTicks = PathRetryTicks;
        return false;
    }

    private void BeginAttack(Monster target)
    {
        attackTarget = target;
        attackAnimationTicks = AttackAnimationTicks;
        attackImpactApplied = false;
        npc.faceDirection(FacingDirection(npc.TilePoint, target.TilePoint));
        npc.Halt();
        monitor.Log(
            $"invite_mine_guard attack_started npc={npc.Name} target={target.Name}.",
            LogLevel.Debug);
    }

    private void UpdateAttackAnimation(GameLocation location)
    {
        Monster? target = attackTarget;
        if (target is null
            || target.Health <= 0
            || target.IsInvisible
            || !ReferenceEquals(target.currentLocation, location))
        {
            StopAttackAnimation();
            return;
        }

        npc.faceDirection(FacingDirection(npc.TilePoint, target.TilePoint));
        npc.Halt();
        attackAnimationTicks--;
        if (!attackImpactApplied && attackAnimationTicks <= AttackAnimationTicks - AttackImpactTick)
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
        Rectangle area = npc.GetBoundingBox();
        area.Inflate(Game1.tileSize / 2, Game1.tileSize / 2);
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
        => Math.Clamp(18 - weapon.Speed, 6, 18);

    private void Finish(string reason)
    {
        if (complete)
            return;
        StopNavigation();
        StopCombatNavigation();
        StopAttackAnimation();
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
