using System.Text.Json;

namespace VivantValley.Services;

/// <summary>Loads and indexes authored story nodes without depending on game state.</summary>
public sealed class StoryCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, StoryDefinition> byId;
    private readonly Dictionary<string, IReadOnlyList<StoryDefinition>> byNpc;

    private StoryCatalog(
        Dictionary<string, StoryDefinition> byId,
        Dictionary<string, IReadOnlyList<StoryDefinition>> byNpc,
        IReadOnlyList<string> issues)
    {
        this.byId = byId;
        this.byNpc = byNpc;
        Issues = issues;
    }

    public static StoryCatalog Empty { get; } = Create(Array.Empty<StoryDefinition>());

    public int Count => byId.Count;

    public IReadOnlyList<string> Issues { get; }

    public static StoryCatalog LoadDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Story directory cannot be empty.", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
        {
            return new StoryCatalog(
                new Dictionary<string, StoryDefinition>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, IReadOnlyList<StoryDefinition>>(StringComparer.OrdinalIgnoreCase),
                new[] { $"Story directory does not exist: {directoryPath}" });
        }

        var definitions = new List<StoryDefinition>();
        var issues = new List<string>();
        foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string json = File.ReadAllText(filePath);
                StoryDefinition? definition = JsonSerializer.Deserialize<StoryDefinition>(json, JsonOptions);
                if (definition is null)
                {
                    issues.Add($"{filePath}: file did not contain a story object.");
                    continue;
                }

                IReadOnlyList<string> validationIssues = Validate(definition);
                if (validationIssues.Count > 0)
                {
                    issues.AddRange(validationIssues.Select(issue => $"{filePath}: {issue}"));
                    continue;
                }

                Normalize(definition);
                definitions.Add(definition);
            }
            catch (Exception ex)
            {
                issues.Add($"{filePath}: {ex.Message}");
            }
        }

        return Create(definitions, issues);
    }

    public static StoryCatalog Create(IEnumerable<StoryDefinition> definitions)
        => Create(definitions, Array.Empty<string>());

    public bool TryGet(string storyId, out StoryDefinition? definition)
    {
        definition = null;
        return !string.IsNullOrWhiteSpace(storyId)
               && byId.TryGetValue(storyId.Trim(), out definition);
    }

    public IReadOnlyList<StoryDefinition> GetForNpc(string npcName)
    {
        return !string.IsNullOrWhiteSpace(npcName)
               && byNpc.TryGetValue(npcName.Trim(), out IReadOnlyList<StoryDefinition>? definitions)
            ? definitions
            : Array.Empty<StoryDefinition>();
    }

    public StoryDefinition? GetFirstForNpc(string npcName)
        => GetForNpc(npcName).FirstOrDefault();

    public StoryDefinition? GetFirst()
        => byId.Values
            .Where(story => story.Enabled)
            .OrderByDescending(story => story.Priority)
            .ThenBy(story => story.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

    public static IReadOnlyList<string> Validate(StoryDefinition definition)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(definition.Id))
            issues.Add("id is required.");
        else if (definition.Id.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '_' or '-')))
            issues.Add("id may only contain letters, digits, '.', '_' and '-'.");
        if (definition.Version < 1)
            issues.Add("version must be at least 1.");
        if (string.IsNullOrWhiteSpace(definition.Npc))
            issues.Add("npc is required.");
        if (definition.Trigger is null)
            issues.Add("trigger is required.");
        if (definition.Scene is null)
            issues.Add("scene is required.");
        if (definition.Choices is null)
            issues.Add("choices cannot be null.");
        if (definition.AcceptEffects is null)
            issues.Add("acceptEffects is required.");

        if (definition.Trigger is not null)
        {
            if (definition.Trigger.MinHearts is < 0 or > 14)
                issues.Add("trigger.minHearts must be between 0 and 14.");
            if (definition.Trigger.MinConversationTurns < 1)
                issues.Add("trigger.minConversationTurns must be at least 1.");
            if (definition.Trigger.DelayDays is < 0 or > 28)
                issues.Add("trigger.delayDays must be between 0 and 28.");
            if (definition.Trigger.ExpiryDays is < 1 or > 28)
                issues.Add("trigger.expiryDays must be between 1 and 28.");
            if (definition.Trigger.CooldownDays is < 0 or > 112)
                issues.Add("trigger.cooldownDays must be between 0 and 112.");
        }

        if (definition.Scene is not null)
        {
            if (definition.Scene.StartTime is < 600 or > 2600
                || definition.Scene.EndTime is < 600 or > 2600
                || definition.Scene.StartTime > definition.Scene.EndTime)
            {
                issues.Add("scene start/end time is invalid.");
            }
            if (definition.Scene.ActivationDistanceTiles is < 1f or > 16f)
                issues.Add("scene.activationDistanceTiles must be between 1 and 16.");
            if (string.IsNullOrWhiteSpace(definition.Scene.AiBrief))
                issues.Add("scene.aiBrief is required.");
            if (string.IsNullOrWhiteSpace(definition.Scene.FallbackText))
                issues.Add("scene.fallbackText is required.");
            if (string.IsNullOrWhiteSpace(definition.Scene.AcceptText))
                issues.Add("scene.acceptText is required.");
            if (string.IsNullOrWhiteSpace(definition.Scene.DeferText))
                issues.Add("scene.deferText is required.");
        }

        if (definition.Choices is { Count: > 0 })
        {
            if (definition.Choices.Count is < 2 or > 4)
                issues.Add("choices must contain between 2 and 4 entries.");

            var choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int deferCount = 0;
            int resolvingCount = 0;
            foreach (StoryChoiceDefinition? choice in definition.Choices)
            {
                if (choice is null)
                {
                    issues.Add("choices cannot contain null entries.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(choice.Id))
                    issues.Add("choice.id is required.");
                else if (!choiceIds.Add(choice.Id.Trim()))
                    issues.Add($"duplicate choice id '{choice.Id}'.");
                if (string.IsNullOrWhiteSpace(choice.Text))
                    issues.Add($"choice '{choice.Id}' text is required.");
                if (choice.Effects is null)
                    issues.Add($"choice '{choice.Id}' effects are required.");

                if (choice.Defer)
                {
                    deferCount++;
                    if (choice.ReceiveGift || !string.IsNullOrWhiteSpace(choice.NextStoryId))
                        issues.Add($"defer choice '{choice.Id}' cannot receive a gift or schedule a next story.");
                }
                else
                    resolvingCount++;

                if (choice.ReceiveGift && string.IsNullOrWhiteSpace(definition.Scene?.GiftItemId))
                    issues.Add($"choice '{choice.Id}' receives a gift but scene.giftItemId is empty.");
            }

            if (deferCount > 1)
                issues.Add("choices may contain at most one defer entry.");
            if (resolvingCount == 0)
                issues.Add("choices must contain at least one resolving entry.");
        }
        else if (definition.Scene is not null && string.IsNullOrWhiteSpace(definition.Scene.GiftItemId))
        {
            issues.Add("legacy stories without choices require scene.giftItemId.");
        }

        return issues;
    }

    private static StoryCatalog Create(IEnumerable<StoryDefinition> definitions, IEnumerable<string> initialIssues)
    {
        var byId = new Dictionary<string, StoryDefinition>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>(initialIssues);
        foreach (StoryDefinition definition in definitions)
        {
            IReadOnlyList<string> validationIssues = Validate(definition);
            if (validationIssues.Count > 0)
            {
                issues.AddRange(validationIssues.Select(issue => $"{definition.Id}: {issue}"));
                continue;
            }

            Normalize(definition);
            if (!byId.TryAdd(definition.Id, definition))
                issues.Add($"Duplicate story id '{definition.Id}' was ignored.");
        }

        foreach (StoryDefinition definition in byId.Values)
        {
            foreach (StoryChoiceDefinition choice in definition.Choices)
            {
                if (!string.IsNullOrWhiteSpace(choice.NextStoryId)
                    && !byId.ContainsKey(choice.NextStoryId))
                {
                    issues.Add($"Story '{definition.Id}' choice '{choice.Id}' references missing next story '{choice.NextStoryId}'.");
                }
            }
        }

        Dictionary<string, IReadOnlyList<StoryDefinition>> byNpc = byId.Values
            .Where(story => story.Enabled)
            .GroupBy(story => story.Npc, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<StoryDefinition>)group
                    .OrderByDescending(story => story.Priority)
                    .ThenBy(story => story.Id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        return new StoryCatalog(byId, byNpc, issues);
    }

    private static void Normalize(StoryDefinition definition)
    {
        definition.Id = definition.Id.Trim();
        definition.Npc = definition.Npc.Trim();
        definition.Trigger.RequiredFlags = NormalizeSet(definition.Trigger.RequiredFlags);
        definition.Trigger.ForbiddenFlags = NormalizeSet(definition.Trigger.ForbiddenFlags);
        definition.Scene.GiftItemId = definition.Scene.GiftItemId?.Trim() ?? string.Empty;
        definition.Scene.AiBrief = definition.Scene.AiBrief.Trim();
        definition.Scene.FallbackText = definition.Scene.FallbackText.Trim();
        definition.Scene.AcceptText = definition.Scene.AcceptText.Trim();
        definition.Scene.DeferText = definition.Scene.DeferText.Trim();
        definition.AcceptEffects.SetFlags = NormalizeSet(definition.AcceptEffects.SetFlags);
        definition.Choices ??= new List<StoryChoiceDefinition>();
        foreach (StoryChoiceDefinition choice in definition.Choices)
        {
            choice.Id = choice.Id.Trim();
            choice.Text = choice.Text.Trim();
            choice.MemoryText = string.IsNullOrWhiteSpace(choice.MemoryText)
                ? choice.Text
                : choice.MemoryText.Trim();
            choice.NextStoryId = choice.NextStoryId?.Trim() ?? string.Empty;
            choice.Effects ??= new StoryEffectsDefinition();
            choice.Effects.SetFlags = NormalizeSet(choice.Effects.SetFlags);
        }
    }

    private static HashSet<string> NormalizeSet(IEnumerable<string>? values)
        => new(
            (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()),
            StringComparer.Ordinal);
}
