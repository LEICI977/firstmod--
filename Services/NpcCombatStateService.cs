using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Tools;

namespace VivantValley.Services;

/// <summary>Owns persisted NPC equipment, hit points, and the hospital lifecycle.</summary>
public sealed class NpcCombatStateService
{
    public const string HospitalLocationName = "Harvey's Clinic";
    public const string DefaultWeaponQualifiedItemId = "(W)4";
    public const int DefaultMaxHealth = 300;
    public const int FriendshipLossOnDefeat = 125;

    private readonly IMonitor monitor;
    private NpcCombatStateStore store = new();
    private bool worldDefaultsInitialized;

    public NpcCombatStateService(IMonitor monitor)
    {
        this.monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
    }

    public event Action<NpcCombatDefeatEvent>? Defeated;

    public NpcCombatStateStore Store => store;

    public void Load(NpcCombatStateStore? loaded)
    {
        store = loaded ?? new NpcCombatStateStore();
        store.Normalize();
        worldDefaultsInitialized = false;
    }

    public void Reset()
    {
        store = new NpcCombatStateStore();
        worldDefaultsInitialized = false;
    }

    /// <summary>Upgrade legacy saved weapon assignments without touching player inventory.</summary>
    public void EnsureAllDefaultWeapons()
    {
        foreach (string npcName in store.Npcs.Keys.ToArray())
            EnsureDefaultWeapon(npcName);
    }

    /// <summary>Initialize every loaded villager before conversation tools are exposed.</summary>
    public bool InitializeDefaultWeaponsForWorld()
    {
        if (worldDefaultsInitialized)
            return false;
        if (!Context.IsWorldReady || Game1.locations.Count == 0)
            return false;

        foreach (NPC npc in Game1.locations
                     .SelectMany(location => location.characters.OfType<NPC>())
                     .Where(candidate => candidate.IsVillager && !candidate.IsMonster))
        {
            EnsureDefaultWeapon(npc.Name);
        }

        worldDefaultsInitialized = true;
        return true;
    }

    public bool IsHospitalized(string? npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName)
            || !store.TryGet(npcName, out NpcCombatState? state)
            || state is null)
        {
            return false;
        }

        return state.HospitalReleaseDay > Game1.Date.TotalDays;
    }

    public bool HasUsableWeapon(string? npcName)
    {
        return !string.IsNullOrWhiteSpace(npcName)
               && EnsureDefaultWeapon(npcName)
               && store.TryGet(npcName, out NpcCombatState? state)
               && state?.Weapon is { } weapon
               && weapon.QualifiedItemId.Equals(DefaultWeaponQualifiedItemId, StringComparison.OrdinalIgnoreCase);
    }

    public NpcWeaponSnapshot? GetWeapon(string npcName)
    {
        if (!EnsureDefaultWeapon(npcName)
            || !store.TryGet(npcName, out NpcCombatState? state)
            || state?.Weapon is null)
            return null;

        NpcWeaponSnapshot snapshot = state.Weapon.Clone();
        snapshot.Normalize();
        return snapshot.QualifiedItemId.Length == 0 ? null : snapshot;
    }

    /// <summary>Ensure every NPC uses a private snapshot of the vanilla Galaxy Sword.</summary>
    public bool EnsureDefaultWeapon(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
            return false;

        NpcCombatState state = GetOrCreate(npcName);
        state.Weapon?.Normalize();
        if (state.Weapon is { QualifiedItemId.Length: > 0 } existing
            && existing.QualifiedItemId.Equals(DefaultWeaponQualifiedItemId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        NpcWeaponSnapshot? defaultWeapon = CreateDefaultWeaponSnapshot();
        if (defaultWeapon is null)
        {
            monitor.Log(
                $"NPC default weapon creation failed for {npcName}; Galaxy Sword {DefaultWeaponQualifiedItemId} was unavailable.",
                LogLevel.Error);
            return false;
        }

        state.Weapon = defaultWeapon;
        monitor.Log(
            $"NPC default weapon assigned npc={npcName} weapon={defaultWeapon.DisplayName} id={defaultWeapon.QualifiedItemId}.",
            LogLevel.Debug);
        return true;
    }

    public NpcCombatState GetOrCreate(string npcName)
    {
        NpcCombatState state = store.GetOrCreate(npcName);
        if (state.MaxHealth < DefaultMaxHealth)
        {
            int oldMaxHealth = Math.Max(1, state.MaxHealth);
            int oldCurrentHealth = state.CurrentHealth;
            state.MaxHealth = DefaultMaxHealth;
            state.CurrentHealth = state.HospitalReleaseDay > Game1.Date.TotalDays
                ? 0
                : oldCurrentHealth <= 0
                    ? state.MaxHealth
                    : Math.Clamp(
                        (int)Math.Round(oldCurrentHealth * (DefaultMaxHealth / (double)oldMaxHealth)),
                        1,
                        state.MaxHealth);
        }
        if (state.MaxHealth <= 0)
            state.MaxHealth = DefaultMaxHealth;
        if (state.CurrentHealth <= 0 && state.HospitalReleaseDay == 0)
            state.CurrentHealth = state.MaxHealth;
        return state;
    }

    public MeleeWeapon? CreateWeaponItem(string npcName)
    {
        NpcWeaponSnapshot? snapshot = GetWeapon(npcName);
        if (snapshot is null)
            return null;

        try
        {
            Item item = ItemRegistry.Create(snapshot.QualifiedItemId, 1, snapshot.Quality, allowNull: true);
            return item as MeleeWeapon;
        }
        catch (Exception exception)
        {
            monitor.Log($"NPC weapon preview failed for {npcName}: {exception.Message}", LogLevel.Warn);
            return null;
        }
    }

    public bool ApplyDamage(NPC npc, Farmer player, int damage, string source)
    {
        ArgumentNullException.ThrowIfNull(npc);
        ArgumentNullException.ThrowIfNull(player);
        if (!Context.IsWorldReady || !Game1.IsMasterGame || IsHospitalized(npc.Name))
            return false;

        NpcCombatState state = GetOrCreate(npc.Name);
        if (!EnsureDefaultWeapon(npc.Name) || state.Weapon is null)
            return false;

        int boundedDamage = Math.Clamp(damage, 1, state.MaxHealth);
        state.CurrentHealth = Math.Max(0, state.CurrentHealth - boundedDamage);
        if (state.CurrentHealth > 0)
            return false;

        Hospitalize(npc, player, source);
        return true;
    }

    private NpcWeaponSnapshot? CreateDefaultWeaponSnapshot()
    {
        try
        {
            Item? item = ItemRegistry.Create(DefaultWeaponQualifiedItemId, 1, 0, allowNull: true);
            if (item is not MeleeWeapon weapon)
            {
                monitor.Log(
                    $"Galaxy Sword item registry returned an invalid item; using canonical fallback stats.",
                    LogLevel.Warn);
                return CreateCanonicalGalaxySwordSnapshot();
            }

            var data = weapon.GetData();
            NpcWeaponSnapshot snapshot = new()
            {
                QualifiedItemId = weapon.QualifiedItemId,
                DisplayName = weapon.DisplayName,
                MinDamage = data.MinDamage,
                MaxDamage = data.MaxDamage,
                Speed = data.Speed,
                CritChance = data.CritChance,
                CritMultiplier = data.CritMultiplier,
                Knockback = data.Knockback,
                AreaOfEffect = data.AreaOfEffect,
                UpgradeLevel = weapon.UpgradeLevel,
                Quality = weapon.Quality,
                SpriteIndex = weapon.CurrentParentTileIndex,
            };
            snapshot.Normalize();
            return snapshot;
        }
        catch (Exception exception)
        {
            monitor.Log($"NPC Galaxy Sword snapshot creation failed; using canonical fallback stats: {exception.Message}", LogLevel.Warn);
            return CreateCanonicalGalaxySwordSnapshot();
        }
    }

    private static NpcWeaponSnapshot CreateCanonicalGalaxySwordSnapshot()
        => new()
        {
            QualifiedItemId = DefaultWeaponQualifiedItemId,
            DisplayName = "银河剑",
            MinDamage = 60,
            MaxDamage = 80,
            Speed = 4,
            CritChance = 0.02f,
            CritMultiplier = 3f,
            Knockback = 1f,
            AreaOfEffect = 0,
            UpgradeLevel = 0,
            Quality = 0,
            SpriteIndex = 4,
        };

    /// <summary>Re-applies the invisible hospital presentation after a save load or unexpected vanilla schedule update.</summary>
    public void Update()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        foreach (NpcCombatState state in store.Npcs.Values.ToArray())
        {
            if (state.HospitalReleaseDay <= Game1.Date.TotalDays)
                continue;

            NPC? npc = Game1.getCharacterFromName(state.NpcName, mustBeVillager: false, includeEventActors: true);
            if (npc is not null && (!npc.IsInvisible || !IsHospitalLocation(npc.currentLocation)))
                ApplyHospitalPresentation(npc, state);
        }
    }

    public void OnDayStarted()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        foreach (NpcCombatState state in store.Npcs.Values.ToArray())
        {
            if (state.HospitalReleaseDay <= 0)
                continue;

            NPC? npc = Game1.getCharacterFromName(state.NpcName, mustBeVillager: false, includeEventActors: true);
            if (state.HospitalReleaseDay <= Game1.Date.TotalDays)
            {
                if (npc is not null)
                    Recover(npc, state);
            }
            else if (npc is not null)
            {
                ApplyHospitalPresentation(npc, state);
            }
        }
    }

    private void Hospitalize(NPC npc, Farmer player, string source)
    {
        NpcCombatState state = GetOrCreate(npc.Name);
        if (state.HospitalReleaseDay > Game1.Date.TotalDays)
            return;

        string locationName = npc.currentLocation?.NameOrUniqueName ?? string.Empty;
        state.CurrentHealth = 0;
        state.HospitalReleaseDay = checked(Game1.Date.TotalDays + 2);
        state.DefeatCount = checked(state.DefeatCount + 1);
        state.LastDefeatDate = $"{Game1.Date} {Game1.timeOfDay}";

        if (player.friendshipData.TryGetValue(npc.Name, out Friendship? friendship) && friendship is not null)
            friendship.Points = Math.Max(0, friendship.Points - FriendshipLossOnDefeat);

        ApplyHospitalPresentation(npc, state);
        monitor.Log(
            $"npc_combat defeated npc={npc.Name} source={source} hospital_release_day={state.HospitalReleaseDay} "
            + $"friendship_loss={FriendshipLossOnDefeat}.",
            LogLevel.Warn);
        try
        {
            Defeated?.Invoke(new NpcCombatDefeatEvent(
                npc.Name,
                npc.displayName,
                state.LastDefeatDate,
                state.HospitalReleaseDay,
                FriendshipLossOnDefeat,
                locationName));
        }
        catch (Exception exception)
        {
            monitor.Log($"NPC defeat memory notification failed for {npc.Name}: {exception}", LogLevel.Error);
        }
    }

    private void ApplyHospitalPresentation(NPC npc, NpcCombatState state)
    {
        if (state.HospitalReleaseDay <= Game1.Date.TotalDays)
            return;

        if (!state.HospitalOriginalVisibilityCaptured)
        {
            state.WasInvisibleBeforeHospital = npc.IsInvisible;
            state.HospitalOriginalVisibilityCaptured = true;
        }

        npc.ignoreScheduleToday = true;
        npc.controller = null;
        npc.temporaryController = null;
        npc.Halt();
        GameLocation? hospital = FindHospital();
        if (hospital is not null && !ReferenceEquals(npc.currentLocation, hospital))
        {
            Point tile = FindSafeHospitalTile(hospital, npc);
            Game1.warpCharacter(npc, hospital, new Vector2(tile.X, tile.Y));
        }

        npc.IsInvisible = true;
    }

    private void Recover(NPC npc, NpcCombatState state)
    {
        state.HospitalReleaseDay = 0;
        state.CurrentHealth = state.MaxHealth;
        state.LastDefeatDate = state.LastDefeatDate ?? string.Empty;
        npc.IsInvisible = state.WasInvisibleBeforeHospital;
        state.WasInvisibleBeforeHospital = false;
        state.HospitalOriginalVisibilityCaptured = false;
        npc.ignoreScheduleToday = false;
        npc.controller = null;
        npc.temporaryController = null;
        npc.queuedSchedulePaths.Clear();
        npc.lastAttemptedSchedule = -1;
        try
        {
            npc.checkSchedule(Game1.timeOfDay);
        }
        catch (Exception exception)
        {
            monitor.Log($"NPC hospital schedule recovery failed for {npc.Name}: {exception.Message}", LogLevel.Debug);
        }

        monitor.Log($"npc_combat hospital_recovered npc={npc.Name} day={Game1.Date.TotalDays}.", LogLevel.Info);
    }

    private static GameLocation? FindHospital()
    {
        foreach (string name in new[] { HospitalLocationName, "Hospital", "Clinic" })
        {
            GameLocation? location = Game1.getLocationFromName(name);
            if (location is not null)
                return location;
        }

        return Game1.locations.FirstOrDefault(location =>
            location.NameOrUniqueName.Contains("Hospital", StringComparison.OrdinalIgnoreCase)
            || location.NameOrUniqueName.Contains("Clinic", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHospitalLocation(GameLocation? location)
        => location is not null
           && (location.NameOrUniqueName.Contains("Hospital", StringComparison.OrdinalIgnoreCase)
               || location.NameOrUniqueName.Contains("Clinic", StringComparison.OrdinalIgnoreCase));

    private static Point FindSafeHospitalTile(GameLocation hospital, NPC npc)
    {
        Point[] candidates =
        {
            new(8, 8), new(9, 8), new(7, 8), new(8, 9), new(9, 9),
            new(10, 8), new(8, 10), new(7, 9), new(10, 9),
        };

        foreach (Point candidate in candidates)
        {
            if (!hospital.isTileOnMap(candidate))
                continue;

            Rectangle bounds = new(candidate.X * Game1.tileSize, candidate.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
            try
            {
                if (!hospital.isCollidingPosition(
                        bounds,
                        Game1.viewport,
                        isFarmer: false,
                        damagesFarmer: 0,
                        glider: false,
                        character: npc,
                        pathfinding: true,
                        projectile: false,
                        ignoreCharacterRequirement: true,
                        skipCollisionEffects: true))
                {
                    return candidate;
                }
            }
            catch
            {
                // Continue searching and use the stable fallback below.
            }
        }

        try
        {
            int width = hospital.Map.Layers[0].LayerWidth;
            int height = hospital.Map.Layers[0].LayerHeight;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Point candidate = new(x, y);
                    Rectangle bounds = new(x * Game1.tileSize, y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
                    if (!hospital.isCollidingPosition(
                            bounds,
                            Game1.viewport,
                            isFarmer: false,
                            damagesFarmer: 0,
                            glider: false,
                            character: npc,
                            pathfinding: true,
                            projectile: false,
                            ignoreCharacterRequirement: true,
                            skipCollisionEffects: true))
                    {
                        return candidate;
                    }
                }
            }
        }
        catch
        {
            // Use the stable fallback when a custom hospital map cannot be inspected.
        }

        return new Point(8, 8);
    }
}
