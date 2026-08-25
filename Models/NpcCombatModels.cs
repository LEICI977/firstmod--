using System.Text.Json.Serialization;

namespace VivantValley;

/// <summary>Per-save combat equipment and hospital state for villagers.</summary>
public sealed class NpcCombatStateStore
{
    public Dictionary<string, NpcCombatState> Npcs { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        Npcs ??= new Dictionary<string, NpcCombatState>(StringComparer.OrdinalIgnoreCase);
        var normalized = new Dictionary<string, NpcCombatState>(StringComparer.OrdinalIgnoreCase);
        foreach ((string? key, NpcCombatState? value) in Npcs.ToArray())
        {
            if (value is null)
                continue;

            string name = string.IsNullOrWhiteSpace(value.NpcName) ? key?.Trim() ?? string.Empty : value.NpcName.Trim();
            if (name.Length == 0)
                continue;

            value.NpcName = name;
            value.MaxHealth = Math.Clamp(value.MaxHealth, 1, 10000);
            value.CurrentHealth = Math.Clamp(value.CurrentHealth, 0, value.MaxHealth);
            value.HospitalReleaseDay = Math.Max(0, value.HospitalReleaseDay);
            value.DefeatCount = Math.Max(0, value.DefeatCount);
            value.Weapon?.Normalize();
            normalized[name] = value;
        }

        Npcs = normalized;
    }

    public NpcCombatState GetOrCreate(string npcName)
    {
        if (string.IsNullOrWhiteSpace(npcName))
            throw new ArgumentException("NPC name cannot be empty.", nameof(npcName));

        Normalize();
        string name = npcName.Trim();
        if (!Npcs.TryGetValue(name, out NpcCombatState? state) || state is null)
        {
            state = new NpcCombatState { NpcName = name };
            Npcs[name] = state;
        }

        state.NpcName = name;
        state.MaxHealth = Math.Clamp(state.MaxHealth, 1, 10000);
        state.CurrentHealth = Math.Clamp(state.CurrentHealth, 0, state.MaxHealth);
        return state;
    }

    public bool TryGet(string npcName, out NpcCombatState? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(npcName))
            return false;

        Normalize();
        return Npcs.TryGetValue(npcName.Trim(), out state) && state is not null;
    }
}

public sealed class NpcCombatState
{
    public string NpcName { get; set; } = string.Empty;

    public NpcWeaponSnapshot? Weapon { get; set; }

    public int MaxHealth { get; set; } = 1200;

    public int CurrentHealth { get; set; } = 1200;

    /// <summary>First day on which normal behavior is allowed again; zero means not hospitalized.</summary>
    public int HospitalReleaseDay { get; set; }

    public int DefeatCount { get; set; }

    public string LastDefeatDate { get; set; } = string.Empty;

    public bool WasInvisibleBeforeHospital { get; set; }

    public bool HospitalOriginalVisibilityCaptured { get; set; }

    [JsonIgnore]
    public bool IsHospitalized => HospitalReleaseDay > 0;
}

/// <summary>A serializable copy of the NPC's default melee weapon. It never owns a player's item.</summary>
public sealed class NpcWeaponSnapshot
{
    public string QualifiedItemId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public int MinDamage { get; set; } = 2;

    public int MaxDamage { get; set; } = 4;

    public int Speed { get; set; }

    public float CritChance { get; set; }

    public float CritMultiplier { get; set; } = 3f;

    public float Knockback { get; set; }

    public int AreaOfEffect { get; set; }

    public int UpgradeLevel { get; set; }

    public int Quality { get; set; }

    public int SpriteIndex { get; set; }

    public void Normalize()
    {
        QualifiedItemId = (QualifiedItemId ?? string.Empty).Trim();
        DisplayName = (DisplayName ?? string.Empty).Trim();
        MinDamage = Math.Clamp(MinDamage, 1, 1000);
        MaxDamage = Math.Clamp(Math.Max(MinDamage, MaxDamage), MinDamage, 2000);
        Speed = Math.Clamp(Speed, -20, 20);
        CritChance = Math.Clamp(CritChance, 0f, 1f);
        CritMultiplier = Math.Clamp(CritMultiplier <= 0 ? 3f : CritMultiplier, 1f, 20f);
        Knockback = Math.Clamp(Knockback, 0f, 20f);
        AreaOfEffect = Math.Clamp(AreaOfEffect, 0, 20);
        UpgradeLevel = Math.Clamp(UpgradeLevel, 0, 20);
        Quality = Math.Clamp(Quality, 0, 4);
    }

    public NpcWeaponSnapshot Clone()
        => new()
        {
            QualifiedItemId = QualifiedItemId,
            DisplayName = DisplayName,
            MinDamage = MinDamage,
            MaxDamage = MaxDamage,
            Speed = Speed,
            CritChance = CritChance,
            CritMultiplier = CritMultiplier,
            Knockback = Knockback,
            AreaOfEffect = AreaOfEffect,
            UpgradeLevel = UpgradeLevel,
            Quality = Quality,
            SpriteIndex = SpriteIndex,
        };
}

public sealed record NpcCombatDefeatEvent(
    string NpcName,
    string NpcDisplayName,
    string GameDate,
    int HospitalReleaseDay,
    int FriendshipLoss,
    string LocationName);
