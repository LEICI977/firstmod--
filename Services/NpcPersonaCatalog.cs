using System.Text.Json;

namespace VivantValley.Services;

/// <summary>Loads authored NPC persona guidance separately from live game facts.</summary>
public sealed class NpcPersonaCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly Dictionary<string, NpcPersonaProfile> profiles;

    private NpcPersonaCatalog(
        Dictionary<string, NpcPersonaProfile> profiles,
        IReadOnlyList<string> issues)
    {
        this.profiles = profiles;
        Issues = issues;
    }

    public static NpcPersonaCatalog Empty { get; } = Create(Array.Empty<NpcPersonaProfile>());

    public IReadOnlyList<string> Issues { get; }

    public static NpcPersonaCatalog LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("NPC persona file path cannot be empty.", nameof(path));

        try
        {
            string json = File.ReadAllText(path);
            return LoadFromJson(json, path);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            return new NpcPersonaCatalog(
                new Dictionary<string, NpcPersonaProfile>(StringComparer.OrdinalIgnoreCase),
                new[] { $"{path}: {exception.Message}" });
        }
    }

    public static NpcPersonaCatalog LoadFromJson(string json, string source = "persona catalog")
    {
        if (string.IsNullOrWhiteSpace(json))
            return new NpcPersonaCatalog(
                new Dictionary<string, NpcPersonaProfile>(StringComparer.OrdinalIgnoreCase),
                new[] { $"{source}: file was empty." });

        try
        {
            NpcPersonaFile? file = JsonSerializer.Deserialize<NpcPersonaFile>(json, JsonOptions);
            if (file is null)
                return new NpcPersonaCatalog(
                    new Dictionary<string, NpcPersonaProfile>(StringComparer.OrdinalIgnoreCase),
                    new[] { $"{source}: root object was missing." });

            var issues = new List<string>();
            var valid = new List<NpcPersonaProfile>();
            foreach (NpcPersonaProfile? profile in file.Personas ?? new List<NpcPersonaProfile>())
            {
                if (profile is null || string.IsNullOrWhiteSpace(profile.NpcName))
                {
                    issues.Add($"{source}: every persona requires npcName.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(profile.CoreIdentity)
                    || string.IsNullOrWhiteSpace(profile.Voice)
                    || string.IsNullOrWhiteSpace(profile.Values)
                    || string.IsNullOrWhiteSpace(profile.Interests)
                    || string.IsNullOrWhiteSpace(profile.Boundaries)
                    || string.IsNullOrWhiteSpace(profile.RelationshipStyle))
                {
                    issues.Add($"{source}: persona '{profile.NpcName}' is missing required guidance.");
                    continue;
                }

                profile.NpcName = profile.NpcName.Trim();
                valid.Add(profile);
            }

            if (valid.Count == 0 && issues.Count == 0)
                issues.Add($"{source}: no personas were defined.");
            return Create(valid, issues);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return new NpcPersonaCatalog(
                new Dictionary<string, NpcPersonaProfile>(StringComparer.OrdinalIgnoreCase),
                new[] { $"{source}: {exception.Message}" });
        }
    }

    public bool TryGet(string npcName, out NpcPersonaProfile? profile)
    {
        profile = null;
        return !string.IsNullOrWhiteSpace(npcName)
               && profiles.TryGetValue(npcName.Trim(), out profile);
    }

    private static NpcPersonaCatalog Create(IEnumerable<NpcPersonaProfile> source)
        => Create(source, Array.Empty<string>());

    private static NpcPersonaCatalog Create(
        IEnumerable<NpcPersonaProfile> source,
        IEnumerable<string> initialIssues)
    {
        var issues = new List<string>(initialIssues);
        var byNpc = new Dictionary<string, NpcPersonaProfile>(StringComparer.OrdinalIgnoreCase);
        foreach (NpcPersonaProfile? profile in source ?? Array.Empty<NpcPersonaProfile>())
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.NpcName))
                continue;
            string key = profile.NpcName.Trim();
            if (!byNpc.TryAdd(key, profile))
                issues.Add($"duplicate persona for NPC '{key}'.");
        }

        return new NpcPersonaCatalog(byNpc, issues);
    }
}

public sealed class NpcPersonaFile
{
    public int SchemaVersion { get; set; } = 1;
    public List<NpcPersonaProfile> Personas { get; set; } = new();
}

public sealed class NpcPersonaProfile
{
    public string NpcName { get; set; } = string.Empty;
    public string CoreIdentity { get; set; } = string.Empty;
    public string Voice { get; set; } = string.Empty;
    public string Values { get; set; } = string.Empty;
    public string Interests { get; set; } = string.Empty;
    public string Boundaries { get; set; } = string.Empty;
    public string RelationshipStyle { get; set; } = string.Empty;

    public string ToPrompt(string displayName)
        => $"【{displayName} 的专属人格（优先于模型常识）】\n"
           + $"- 核心身份：{CoreIdentity.Trim()}\n"
           + $"- 说话方式：{Voice.Trim()}\n"
           + $"- 重视的事：{Values.Trim()}\n"
           + $"- 兴趣与自然话题：{Interests.Trim()}\n"
           + $"- 边界与禁忌：{Boundaries.Trim()}\n"
           + $"- 与玩家相处：{RelationshipStyle.Trim()}";
}
