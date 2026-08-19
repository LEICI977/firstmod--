using System.Text;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Events;
using StardewValley;

namespace VivantValley.Services;

/// <summary>
/// Aggregates high-level player activity into bounded per-day tags. It stores
/// no raw event stream, item IDs, coordinates, or timestamps.
/// </summary>
public sealed class PlayerActivityJournal
{
    private readonly int retentionDays;

    public PlayerActivityJournal(int retentionDays = PlayerSocialDirectorState.MaxActivityDays)
    {
        this.retentionDays = Math.Clamp(
            retentionDays,
            1,
            PlayerSocialDirectorState.MaxActivityDays);
    }

    public bool RecordWarped(PlayerSocialDirectorState state, int day, WarpedEventArgs e)
    {
        if (e is null)
            throw new ArgumentNullException(nameof(e));
        if (!e.IsLocalPlayer)
            return false;

        return RecordWarp(
            state,
            day,
            e.NewLocation?.NameOrUniqueName,
            e.NewLocation?.IsOutdoors ?? false);
    }

    public bool RecordWarp(
        PlayerSocialDirectorState state,
        int day,
        string? newLocationName,
        bool isOutdoors)
    {
        DailyActivitySummary summary = GetOrCreateDay(state, day);
        summary.Add("travel");
        summary.Add("visit:" + ClassifyLocation(newLocationName, isOutdoors));
        Trim(state, day);
        return true;
    }

    public bool RecordInventoryChanged(
        PlayerSocialDirectorState state,
        int day,
        InventoryChangedEventArgs e)
    {
        if (e is null)
            throw new ArgumentNullException(nameof(e));
        if (!e.IsLocalPlayer)
            return false;

        return RecordInventoryChange(state, day, e.Added, e.Removed, e.QuantityChanged);
    }

    public bool RecordInventoryChange(
        PlayerSocialDirectorState state,
        int day,
        IEnumerable<Item>? added,
        IEnumerable<Item>? removed,
        IEnumerable<ItemStackSizeChange>? quantityChanged = null)
    {
        var changes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Item? item in added ?? Array.Empty<Item>())
            AddItemChange(changes, item, item is null ? 0 : Math.Max(1, item.Stack));
        foreach (Item? item in removed ?? Array.Empty<Item>())
            AddItemChange(changes, item, item is null ? 0 : -Math.Max(1, item.Stack));
        foreach (ItemStackSizeChange? change in quantityChanged ?? Array.Empty<ItemStackSizeChange>())
        {
            if (change is null)
                continue;

            long delta = (long)change.NewSize - change.OldSize;
            int boundedDelta = delta < 0
                ? -ClampMagnitude(-delta)
                : delta > 0
                    ? ClampMagnitude(delta)
                    : 0;
            AddItemChange(changes, change.Item, boundedDelta);
        }

        if (changes.Count == 0)
            return false;

        DailyActivitySummary summary = GetOrCreateDay(state, day);
        foreach ((string tag, int count) in changes)
            summary.Add(tag, count);
        Trim(state, day);
        return true;
    }

    public bool RecordLevelChanged(PlayerSocialDirectorState state, int day, LevelChangedEventArgs e)
    {
        if (e is null)
            throw new ArgumentNullException(nameof(e));
        if (!e.IsLocalPlayer)
            return false;

        return RecordLevelChange(state, day, e.Skill, e.OldLevel, e.NewLevel);
    }

    public bool RecordLevelChange(
        PlayerSocialDirectorState state,
        int day,
        SkillType skill,
        int oldLevel,
        int newLevel)
    {
        int delta = newLevel - oldLevel;
        if (delta == 0)
            return false;

        string direction = delta > 0 ? "level_up" : "level_down";
        int magnitude = ClampMagnitude(Math.Abs((long)delta));
        DailyActivitySummary summary = GetOrCreateDay(state, day);
        summary.Add(direction, magnitude);
        summary.Add(direction + ":" + NormalizeSkill(skill), magnitude);
        Trim(state, day);
        return true;
    }

    public bool RecordTimeChanged(PlayerSocialDirectorState state, int day, TimeChangedEventArgs e)
    {
        if (e is null)
            throw new ArgumentNullException(nameof(e));

        return RecordTimeChange(state, day, e.OldTime, e.NewTime);
    }

    public bool RecordTimeChange(
        PlayerSocialDirectorState state,
        int day,
        int oldTime,
        int newTime)
    {
        if (oldTime == newTime)
            return false;

        DailyActivitySummary summary = GetOrCreateDay(state, day);
        summary.Add("active:" + ClassifyTime(newTime));
        if (oldTime < 2400 && newTime >= 2400)
            summary.Add("stayed_up_late");
        Trim(state, day);
        return true;
    }

    /// <summary>Remove out-of-window and duplicate summaries.</summary>
    public bool Trim(PlayerSocialDirectorState state, int currentDay)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        state.ActivityJournal ??= new List<DailyActivitySummary>();
        List<DailyActivitySummary> previous = state.ActivityJournal;
        int earliestDay = Math.Max(0, currentDay - retentionDays + 1);
        List<DailyActivitySummary> normalized = previous
            .Where(summary => summary is not null && summary.Day >= earliestDay && summary.Day <= currentDay)
            .Select(summary =>
            {
                summary.Normalize();
                return summary;
            })
            .GroupBy(summary => summary.Day)
            .Select(group => DailyActivitySummary.Merge(group))
            .OrderByDescending(summary => summary.Day)
            .Take(retentionDays)
            .OrderBy(summary => summary.Day)
            .ToList();

        bool changed = previous.Count != normalized.Count
                       || !previous.Select(summary => summary?.Day ?? -1).SequenceEqual(
                           normalized.Select(summary => summary.Day));
        state.ActivityJournal = normalized;
        return changed;
    }

    /// <summary>Build a small deterministic block suitable for an AI scene prompt.</summary>
    public string BuildPromptSummary(
        PlayerSocialDirectorState state,
        int currentDay,
        int maximumTagsPerDay = 12)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        maximumTagsPerDay = Math.Clamp(maximumTagsPerDay, 1, DailyActivitySummary.MaxTags);
        Trim(state, currentDay);
        var output = new StringBuilder();
        foreach (DailyActivitySummary day in state.ActivityJournal.OrderBy(summary => summary.Day))
        {
            string label = day.Day == currentDay ? "today" : $"{currentDay - day.Day}d ago";
            string tags = string.Join(
                ", ",
                day.ActivityTags
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Take(maximumTagsPerDay)
                    .Select(pair => $"{pair.Key}={pair.Value}"));
            if (tags.Length > 0)
                output.Append(label).Append(": ").AppendLine(tags);
        }

        return output.ToString().TrimEnd();
    }

    private static DailyActivitySummary GetOrCreateDay(PlayerSocialDirectorState state, int day)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));
        if (day < 0)
            throw new ArgumentOutOfRangeException(nameof(day));

        state.ActivityJournal ??= new List<DailyActivitySummary>();
        DailyActivitySummary? summary = state.ActivityJournal.FirstOrDefault(value => value?.Day == day);
        if (summary is null)
        {
            summary = new DailyActivitySummary { Day = day };
            state.ActivityJournal.Add(summary);
        }

        summary.Normalize();
        return summary;
    }

    private static void AddItemChange(Dictionary<string, int> changes, Item? item, int delta)
    {
        if (item is null || delta == 0)
            return;

        string direction = delta > 0 ? "item_gain" : "item_loss";
        int magnitude = ClampMagnitude(Math.Abs((long)delta));
        AddAggregate(changes, direction, magnitude);
        AddAggregate(changes, direction + ":" + ClassifyItem(item), magnitude);
    }

    private static void AddAggregate(Dictionary<string, int> changes, string tag, int count)
    {
        int existing = changes.TryGetValue(tag, out int value) ? value : 0;
        changes[tag] = (int)Math.Min(DailyActivitySummary.MaxTagCount, (long)existing + count);
    }

    private static int ClampMagnitude(long value)
        => (int)Math.Clamp(value, 1L, DailyActivitySummary.MaxTagCount);

    private static string ClassifyLocation(string? locationName, bool isOutdoors)
    {
        string name = (locationName ?? string.Empty).Trim().ToLowerInvariant();
        if (ContainsAny(name, "undergroundmine", "skullcave", "volcanodungeon", "quarrymine"))
            return "mines";
        if (ContainsAny(name, "island", "caldera", "volcano"))
            return "island";
        if (name.Contains("desert", StringComparison.Ordinal))
            return "desert";
        if (ContainsAny(name, "farm", "greenhouse"))
            return "farm";
        if (ContainsAny(name, "beach", "fishshop"))
            return "beach";
        if (ContainsAny(name, "forest", "woods", "sewer", "wizardhouse"))
            return "forest";
        if (ContainsAny(name, "mountain", "railroad", "adventureguild", "summit"))
            return "mountain";
        if (ContainsAny(name, "town", "communitycenter", "jojamart", "hospital", "manorhouse"))
            return "town";
        return isOutdoors ? "outdoors" : "indoors";
    }

    private static string ClassifyItem(Item item)
    {
        if (HasAnyTag(item, "category_fish", "fish_item"))
            return "fish";
        if (HasAnyTag(item, "category_gem", "category_minerals", "mineral_item"))
            return "mineral";
        if (HasAnyTag(item, "category_metal_resources", "category_building_resources", "resource_item"))
            return "resource";
        if (HasAnyTag(item, "category_fruits", "category_vegetable", "category_flowers", "category_greens", "crop_item"))
            return "produce";
        if (HasAnyTag(item, "forage_item"))
            return "forage";
        if (HasAnyTag(item, "category_artisan_goods", "artisan_good"))
            return "artisan";
        if (HasAnyTag(item, "category_cooking", "food_item", "drink_item"))
            return "food";
        if (HasAnyTag(item, "category_seeds", "seed_item"))
            return "seed";
        if (HasAnyTag(item, "category_monster_loot", "monster_loot"))
            return "monster_loot";

        string typeName = item.GetType().Name;
        if (typeName.Contains("Tool", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Weapon", StringComparison.OrdinalIgnoreCase))
        {
            return "equipment";
        }

        return "other";
    }

    private static bool HasAnyTag(Item item, params string[] tags)
    {
        try
        {
            return tags.Any(item.HasContextTag);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeSkill(SkillType skill)
        => skill switch
        {
            SkillType.Farming => "farming",
            SkillType.Fishing => "fishing",
            SkillType.Foraging => "foraging",
            SkillType.Mining => "mining",
            SkillType.Combat => "combat",
            SkillType.Luck => "luck",
            _ => "other",
        };

    private static string ClassifyTime(int time)
        => time switch
        {
            < 1200 => "morning",
            < 1800 => "afternoon",
            < 2200 => "evening",
            _ => "late_night",
        };

    private static bool ContainsAny(string value, params string[] fragments)
        => fragments.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));
}
