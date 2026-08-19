using System.Text.Json;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;

namespace VivantValley.Services;

/// <summary>
/// Builds a deterministic gift allowlist and validates the AI's selected key.
/// The service never accepts an item ID from model output.
/// </summary>
public sealed class GiftPolicyService
{
    public const string TalkOnlyKey = "talk_only";

    private const int TargetGiftBatchValue = 100;
    private const int CurrentCatalogSchemaVersion = 2;
    private const int MaximumCatalogEntries = 768;
    private const int MaximumTemplateEntries = 256;
    private const int MaximumKeyLength = 64;
    private const int MaximumHintLength = 240;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IReadOnlyList<SocialGiftPoolEntry> entries;
    private readonly ISocialGiftItemResolver itemResolver;
    private readonly SocialGiftPolicyOptions options;

    public GiftPolicyService(
        SocialGiftPoolCatalog catalog,
        ISocialGiftItemResolver itemResolver,
        SocialGiftPolicyOptions? options = null)
    {
        this.itemResolver = itemResolver ?? throw new ArgumentNullException(nameof(itemResolver));
        this.options = CopyAndValidateOptions(options ?? new SocialGiftPolicyOptions());
        entries = NormalizeCatalog(catalog, out IReadOnlyList<string> issues);
        CatalogIssues = issues;
    }

    public IReadOnlyList<string> CatalogIssues { get; private set; }

    public SocialGiftPolicyOptions Options => new()
    {
        MaximumCandidateCount = options.MaximumCandidateCount,
    };

    /// <summary>Load a catalog without throwing for malformed JSON. Errors produce an empty, fail-closed catalog.</summary>
    public static GiftPolicyService LoadFromFile(
        string path,
        ISocialGiftItemResolver? itemResolver = null,
        SocialGiftPolicyOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("礼物候选文件路径不能为空。", nameof(path));

        try
        {
            string json = File.ReadAllText(path);
            return LoadFromJson(json, itemResolver, options);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            return CreateFailedService(
                $"无法读取礼物候选文件：{SanitizeIssue(exception.Message)}",
                itemResolver,
                options);
        }
    }

    /// <summary>Parse a catalog from JSON. This overload is deterministic and convenient for tests.</summary>
    public static GiftPolicyService LoadFromJson(
        string json,
        ISocialGiftItemResolver? itemResolver = null,
        SocialGiftPolicyOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return CreateFailedService("礼物候选 JSON 为空。", itemResolver, options);

        try
        {
            SocialGiftPoolCatalog? catalog = JsonSerializer.Deserialize<SocialGiftPoolCatalog>(json, JsonOptions);
            if (catalog is null)
                return CreateFailedService("礼物候选 JSON 没有根对象。", itemResolver, options);

            return new GiftPolicyService(catalog, itemResolver ?? new StardewGiftItemResolver(), options);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return CreateFailedService(
                $"礼物候选 JSON 无法解析：{SanitizeIssue(exception.Message)}",
                itemResolver,
                options);
        }
    }

    /// <summary>Build the only keys that may be offered to the AI for this action.</summary>
    public SocialGiftCandidateSet BuildCandidateSet(GiftPolicyContext context)
    {
        SocialGiftRejectionReason contextBlock = ValidateActionContext(context);
        if (contextBlock != SocialGiftRejectionReason.None)
            return Blocked(contextBlock);

        SocialGiftRejectionReason giftBlock = ValidateGiftBudget(context);
        if (giftBlock != SocialGiftRejectionReason.None)
            return Blocked(giftBlock);

        if (entries.Count == 0)
            return Blocked(SocialGiftRejectionReason.CatalogUnavailable);

        var relevantTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "general" };
        foreach (string? tag in context.RelevantTags ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(tag))
                relevantTags.Add(tag.Trim());
        }

        var accepted = new List<RankedCandidate>();
        var rejected = new List<SocialGiftCandidateRejection>();
        foreach (SocialGiftPoolEntry entry in entries)
        {
            bool npcSpecific = entry.NpcNames.Count > 0;
            if (npcSpecific && !entry.NpcNames.Contains(context.NpcName, StringComparer.OrdinalIgnoreCase))
                continue;

            string[] matchedTags = entry.ApplicableTags
                .Where(relevantTags.Contains)
                .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (entry.ApplicableTags.Count > 0 && matchedTags.Length == 0)
                continue;

            if (!TryCreateCandidate(
                    entry,
                    context,
                    matchedTags,
                    out SocialGiftCandidate? candidate,
                    out SocialGiftRejectionReason reason)
                || candidate is null)
            {
                rejected.Add(new SocialGiftCandidateRejection { Key = entry.Key, Reason = reason });
                continue;
            }

            accepted.Add(new RankedCandidate(
                candidate,
                npcSpecific ? 1 : 0,
                matchedTags.Length,
                entry.Priority));
        }

        RankedCandidate[] ranked = accepted
            .OrderByDescending(item => item.NpcSpecific)
            .ThenByDescending(item => item.MatchedTagCount)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.Candidate.Key, StringComparer.Ordinal)
            .ToArray();
        SocialGiftCandidate[] candidates = SelectBalancedCandidates(ranked, options.MaximumCandidateCount);

        return new SocialGiftCandidateSet
        {
            Candidates = candidates,
            Rejections = rejected,
            BlockReason = candidates.Length == 0
                ? SocialGiftRejectionReason.NoApplicableCandidates
                : SocialGiftRejectionReason.None,
        };
    }

    /// <summary>
    /// Rebuild and revalidate the allowlist after the AI responds. A key not in the current
    /// allowlist is always rejected, even if it resembles an item ID or a catalog key.
    /// </summary>
    public SocialGiftSelectionResult ValidateAiSelection(GiftPolicyContext context, string? selectedKey)
    {
        SocialGiftRejectionReason actionBlock = ValidateActionContext(context);
        if (actionBlock != SocialGiftRejectionReason.None)
            return Rejected(actionBlock);

        string normalizedKey = (selectedKey ?? string.Empty).Trim();
        if (normalizedKey.Length == 0)
            return Rejected(SocialGiftRejectionReason.EmptySelection);

        if (normalizedKey.Equals(TalkOnlyKey, StringComparison.OrdinalIgnoreCase))
        {
            return new SocialGiftSelectionResult
            {
                Kind = SocialGiftSelectionKind.TalkOnly,
                RejectionReason = SocialGiftRejectionReason.None,
            };
        }

        SocialGiftCandidateSet currentSet = BuildCandidateSet(context);
        SocialGiftCandidate? selected = currentSet.Candidates.FirstOrDefault(candidate =>
            candidate.Key.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            return Rejected(
                currentSet.BlockReason == SocialGiftRejectionReason.None
                    ? SocialGiftRejectionReason.UnknownCandidateKey
                    : currentSet.BlockReason);
        }

        return new SocialGiftSelectionResult
        {
            Kind = SocialGiftSelectionKind.Gift,
            RejectionReason = SocialGiftRejectionReason.None,
            Candidate = selected,
        };
    }

    private bool TryCreateCandidate(
        SocialGiftPoolEntry entry,
        GiftPolicyContext context,
        string[] matchedTags,
        out SocialGiftCandidate? candidate,
        out SocialGiftRejectionReason reason)
    {
        candidate = null;
        reason = SocialGiftRejectionReason.None;
        if (context.HeartLevel < entry.MinHearts)
        {
            reason = SocialGiftRejectionReason.RelationshipTooLow;
            return false;
        }
        if (!entry.DeliveryModes.Contains(context.DeliveryMode, StringComparer.OrdinalIgnoreCase))
        {
            reason = SocialGiftRejectionReason.DeliveryModeNotAllowed;
            return false;
        }
        if (!itemResolver.TryResolve(entry.QualifiedItemId, out SocialGiftItemFacts? facts)
            || facts is null
            || !facts.Exists)
        {
            reason = SocialGiftRejectionReason.UnknownItem;
            return false;
        }

        if (facts.IsTool || facts.IsWeapon)
        {
            reason = SocialGiftRejectionReason.ToolOrWeapon;
            return false;
        }
        if (facts.IsQuestOrUnique)
        {
            reason = SocialGiftRejectionReason.QuestOrUniqueItem;
            return false;
        }
        if (!facts.IsObject
            || !string.Equals(facts.TypeDefinitionId, ItemRegistry.type_object, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(facts.QualifiedItemId)
            || !facts.QualifiedItemId.StartsWith(ItemRegistry.type_object, StringComparison.Ordinal))
        {
            reason = SocialGiftRejectionReason.NonObjectItem;
            return false;
        }
        if (!facts.CanBeTrashed)
        {
            reason = SocialGiftRejectionReason.ItemCannotBeTrashed;
            return false;
        }
        if (!facts.CanBeShipped)
        {
            reason = SocialGiftRejectionReason.ItemCannotBeShipped;
            return false;
        }
        if (!facts.CanBeGivenAsGift)
        {
            reason = SocialGiftRejectionReason.ItemCannotBeGivenAsGift;
            return false;
        }

        int economicValue = facts.EconomicValue;
        if (economicValue <= 0)
        {
            reason = SocialGiftRejectionReason.InvalidItemValue;
            return false;
        }
        int repeatCooldownDays = GetEffectiveRepeatCooldownDays(entry);
        if (repeatCooldownDays > 0
            && (context.RecentGifts ?? Array.Empty<NpcGiftHistoryEntry>()).Any(gift =>
                gift is not null
                && gift.QualifiedItemId.Equals(facts.QualifiedItemId, StringComparison.Ordinal)
                && context.CurrentDay >= gift.Day
                && context.CurrentDay - gift.Day < repeatCooldownDays))
        {
            reason = SocialGiftRejectionReason.RecentlyGiven;
            return false;
        }
        candidate = new SocialGiftCandidate
        {
            Key = entry.Key,
            QualifiedItemId = facts.QualifiedItemId,
            DisplayName = facts.DisplayName,
            DisplayHint = entry.DisplayHint,
            ApplicableTags = entry.ApplicableTags.ToArray(),
            MatchedTags = matchedTags,
            EconomicValue = economicValue,
            Quantity = CalculateGiftQuantity(economicValue),
            Category = entry.Category,
            MinHearts = entry.MinHearts,
            RepeatCooldownDays = repeatCooldownDays,
        };
        return true;
    }

    private static SocialGiftCandidate[] SelectBalancedCandidates(
        IReadOnlyList<RankedCandidate> ranked,
        int maximumCandidateCount)
    {
        var selected = new List<RankedCandidate>(maximumCandidateCount);
        var selectedItemIds = new HashSet<string>(StringComparer.Ordinal);
        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var quotas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [SocialGiftCategories.Signature] = 2,
            [SocialGiftCategories.Activity] = 3,
            [SocialGiftCategories.Seasonal] = 2,
            [SocialGiftCategories.Care] = 2,
            [SocialGiftCategories.Fallback] = 1,
            [SocialGiftCategories.General] = 2,
        };

        foreach (RankedCandidate item in ranked)
        {
            string category = NormalizeCategory(item.Candidate.Category);
            int count = categoryCounts.TryGetValue(category, out int current) ? current : 0;
            int quota = quotas.TryGetValue(category, out int configured) ? configured : 2;
            if (count >= quota || !selectedItemIds.Add(item.Candidate.QualifiedItemId))
                continue;

            selected.Add(item);
            categoryCounts[category] = count + 1;
            if (selected.Count >= maximumCandidateCount)
                break;
        }

        if (selected.Count < maximumCandidateCount)
        {
            foreach (RankedCandidate item in ranked)
            {
                if (selected.Contains(item) || !selectedItemIds.Add(item.Candidate.QualifiedItemId))
                    continue;
                selected.Add(item);
                if (selected.Count >= maximumCandidateCount)
                    break;
            }
        }

        return selected.Select(item => item.Candidate).ToArray();
    }

    private static int CalculateGiftQuantity(int economicValue)
        => economicValue >= TargetGiftBatchValue
            ? 1
            : (TargetGiftBatchValue + economicValue - 1) / economicValue;

    private static int GetEffectiveRepeatCooldownDays(SocialGiftPoolEntry entry)
    {
        // Keep generic fallback gifts configurable; all authored gift categories use the short window.
        string category = NormalizeCategory(entry.Category);
        bool ordinary = category.Equals(SocialGiftCategories.General, StringComparison.Ordinal)
                        || category.Equals(SocialGiftCategories.Fallback, StringComparison.Ordinal);
        return ordinary ? Math.Clamp(entry.RepeatCooldownDays, 0, 112) : 3;
    }

    private SocialGiftRejectionReason ValidateGiftBudget(GiftPolicyContext context)
    {
        if (context.GiftAlreadyOfferedToday)
            return SocialGiftRejectionReason.NpcAlreadyOfferedToday;

        return SocialGiftRejectionReason.None;
    }

    private static SocialGiftRejectionReason ValidateActionContext(GiftPolicyContext? context)
    {
        if (context is null
            || string.IsNullOrWhiteSpace(context.ActionId)
            || string.IsNullOrWhiteSpace(context.NpcName)
            || context.CurrentDay < 0)
        {
            return SocialGiftRejectionReason.InvalidContext;
        }

        if ((context.CompletedActionIds ?? Array.Empty<string>()).Any(actionId =>
                !string.IsNullOrWhiteSpace(actionId)
                && actionId.Equals(context.ActionId, StringComparison.Ordinal)))
        {
            return SocialGiftRejectionReason.DuplicateActionId;
        }

        return SocialGiftRejectionReason.None;
    }

    private static IReadOnlyList<SocialGiftPoolEntry> NormalizeCatalog(
        SocialGiftPoolCatalog? catalog,
        out IReadOnlyList<string> issues)
    {
        var normalized = new List<SocialGiftPoolEntry>();
        var errors = new List<string>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (catalog is null)
        {
            errors.Add("礼物候选目录为空。");
            issues = errors;
            return normalized;
        }
        if (catalog.SchemaVersion is not 1 and not CurrentCatalogSchemaVersion)
        {
            errors.Add($"不支持礼物候选 schemaVersion={catalog.SchemaVersion}。");
            issues = errors;
            return normalized;
        }

        List<SocialGiftPoolEntry> source = catalog.SchemaVersion == 1
            ? catalog.Gifts ?? new List<SocialGiftPoolEntry>()
            : ExpandVersionTwoCatalog(catalog, errors);
        if (source.Count > MaximumCatalogEntries)
            errors.Add($"礼物候选超过 {MaximumCatalogEntries} 条，超出部分已忽略。");

        foreach (SocialGiftPoolEntry? raw in source.Take(MaximumCatalogEntries))
        {
            if (raw is null || !raw.Enabled)
                continue;

            string key = (raw.Key ?? string.Empty).Trim();
            if (!IsValidKey(key) || key.Equals(TalkOnlyKey, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"已忽略无效礼物 key：{SanitizeIssue(key)}");
                continue;
            }
            if (!keys.Add(key))
            {
                errors.Add($"已忽略重复礼物 key：{key}");
                continue;
            }

            string itemId = (raw.QualifiedItemId ?? string.Empty).Trim();
            if (!itemId.StartsWith(ItemRegistry.type_object, StringComparison.Ordinal)
                || itemId.Length <= ItemRegistry.type_object.Length)
            {
                errors.Add($"礼物 {key} 不是合格的对象 ID，已忽略。");
                continue;
            }

            string displayHint = NormalizeText(raw.DisplayHint, MaximumHintLength);
            string[] npcNames = NormalizeValues(raw.NpcNames, maximumCount: 32);
            string[] tags = NormalizeValues(raw.ApplicableTags, maximumCount: 24, lowerCase: true);
            if (tags.Length == 0)
            {
                errors.Add($"礼物 {key} 没有适用 tag，已忽略。");
                continue;
            }

            normalized.Add(new SocialGiftPoolEntry
            {
                Key = key,
                QualifiedItemId = itemId,
                DisplayHint = displayHint,
                NpcNames = npcNames.ToList(),
                ApplicableTags = tags.ToList(),
                Priority = Math.Clamp(raw.Priority, -1000, 1000),
                Category = NormalizeCategory(raw.Category),
                MinHearts = Math.Clamp(raw.MinHearts, 0, 14),
                RepeatCooldownDays = Math.Clamp(raw.RepeatCooldownDays, 0, 112),
                DeliveryModes = NormalizeDeliveryModes(raw.DeliveryModes).ToList(),
                Enabled = true,
            });
        }

        issues = errors;
        return normalized;
    }

    private static List<SocialGiftPoolEntry> ExpandVersionTwoCatalog(
        SocialGiftPoolCatalog catalog,
        List<string> errors)
    {
        var templates = new Dictionary<string, SocialGiftItemTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (SocialGiftItemTemplate? template in (catalog.Items ?? new List<SocialGiftItemTemplate>())
                     .Take(MaximumTemplateEntries))
        {
            if (template is null || !template.Enabled)
                continue;
            string key = (template.Key ?? string.Empty).Trim();
            if (!IsValidKey(key) || !templates.TryAdd(key, template))
            {
                errors.Add($"已忽略无效或重复的礼物模板：{SanitizeIssue(key)}");
                continue;
            }
        }
        if ((catalog.Items?.Count ?? 0) > MaximumTemplateEntries)
            errors.Add($"礼物模板超过 {MaximumTemplateEntries} 条，超出部分已忽略。");

        var expanded = new List<SocialGiftPoolEntry>();
        AddTemplateReferences(
            expanded,
            templates,
            catalog.Global,
            npcName: null,
            keyPrefix: "global",
            errors);

        foreach ((string npcName, List<string>? references) in
                 (catalog.NpcPools ?? new Dictionary<string, List<string>>()).Take(64))
        {
            string normalizedNpc = NormalizeText(npcName, 80);
            if (normalizedNpc.Length == 0)
            {
                errors.Add("已忽略名称为空的 NPC 礼物池。");
                continue;
            }
            AddTemplateReferences(
                expanded,
                templates,
                references,
                normalizedNpc,
                ToKeyFragment(normalizedNpc),
                errors);
        }

        MergeLegacyEntries(expanded, catalog.Gifts, errors);

        return expanded;
    }

    private static void MergeLegacyEntries(
        List<SocialGiftPoolEntry> expanded,
        IEnumerable<SocialGiftPoolEntry>? legacyEntries,
        List<string> errors)
    {
        foreach (SocialGiftPoolEntry? legacy in legacyEntries ?? Array.Empty<SocialGiftPoolEntry>())
        {
            if (legacy is null)
                continue;

            string key = (legacy.Key ?? string.Empty).Trim();
            SocialGiftPoolEntry? current = expanded.FirstOrDefault(entry =>
                entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (current is null)
            {
                expanded.Add(legacy);
                continue;
            }
            if (!current.QualifiedItemId.Equals(
                    (legacy.QualifiedItemId ?? string.Empty).Trim(),
                    StringComparison.Ordinal))
            {
                errors.Add($"礼物 key {SanitizeIssue(key)} 的 v2 模板与旧定义物品不一致，旧定义已忽略。");
                continue;
            }

            // The generated key remains stable for pending plans. Merge the broader
            // legacy scope into the richer v2 metadata instead of creating a duplicate.
            List<string> legacyNpcNames = legacy.NpcNames ?? new List<string>();
            if (current.NpcNames.Count > 0 && legacyNpcNames.Count > 0)
            {
                current.NpcNames = current.NpcNames
                    .Concat(legacyNpcNames)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else
            {
                current.NpcNames.Clear();
            }

            current.ApplicableTags = current.ApplicableTags
                .Concat(legacy.ApplicableTags ?? new List<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            current.Priority = Math.Max(current.Priority, legacy.Priority);
            current.MinHearts = Math.Min(current.MinHearts, legacy.MinHearts);
            current.RepeatCooldownDays = Math.Max(current.RepeatCooldownDays, legacy.RepeatCooldownDays);
            current.DeliveryModes = current.DeliveryModes
                .Concat(legacy.DeliveryModes ?? new List<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    private static void AddTemplateReferences(
        List<SocialGiftPoolEntry> output,
        IReadOnlyDictionary<string, SocialGiftItemTemplate> templates,
        IEnumerable<string>? references,
        string? npcName,
        string keyPrefix,
        List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (string reference in (references ?? Array.Empty<string>()).Take(24))
        {
            string templateKey = (reference ?? string.Empty).Trim();
            if (!seen.Add(templateKey))
                continue;
            if (!templates.TryGetValue(templateKey, out SocialGiftItemTemplate? template))
            {
                errors.Add($"礼物池 {keyPrefix} 引用了不存在的模板：{SanitizeIssue(templateKey)}");
                continue;
            }

            output.Add(new SocialGiftPoolEntry
            {
                Key = $"{keyPrefix}_{template.Key}",
                QualifiedItemId = template.QualifiedItemId,
                DisplayHint = template.DisplayHint,
                NpcNames = npcName is null ? new List<string>() : new List<string> { npcName },
                ApplicableTags = template.ApplicableTags?.ToList() ?? new List<string>(),
                Priority = Math.Clamp(template.Priority + Math.Max(0, 20 - index), -1000, 1000),
                Category = template.Category,
                MinHearts = template.MinHearts,
                RepeatCooldownDays = template.RepeatCooldownDays,
                DeliveryModes = template.DeliveryModes?.ToList() ?? new List<string>(),
                Enabled = template.Enabled,
            });
            index++;
        }
    }

    private static string[] NormalizeValues(
        IEnumerable<string>? values,
        int maximumCount,
        bool lowerCase = false)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Select(value => lowerCase ? value.ToLowerInvariant() : value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximumCount)
            .ToArray();
    }

    private static string NormalizeCategory(string? category)
        => category?.Trim().ToLowerInvariant() switch
        {
            SocialGiftCategories.Signature => SocialGiftCategories.Signature,
            SocialGiftCategories.Activity => SocialGiftCategories.Activity,
            SocialGiftCategories.Seasonal => SocialGiftCategories.Seasonal,
            SocialGiftCategories.Care => SocialGiftCategories.Care,
            SocialGiftCategories.Fallback => SocialGiftCategories.Fallback,
            _ => SocialGiftCategories.General,
        };

    private static string[] NormalizeDeliveryModes(IEnumerable<string>? modes)
    {
        string[] normalized = NormalizeValues(modes, maximumCount: 2, lowerCase: true)
            .Where(mode => mode is SocialGiftDeliveryModes.Immediate or SocialGiftDeliveryModes.Mail)
            .ToArray();
        return normalized.Length == 0
            ? new[] { SocialGiftDeliveryModes.Immediate, SocialGiftDeliveryModes.Mail }
            : normalized;
    }

    private static string ToKeyFragment(string value)
    {
        string fragment = new(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' ? character : '_')
            .ToArray());
        return fragment.Trim('_');
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length is < 1 or > MaximumKeyLength)
            return false;
        if (!(key[0] is >= 'a' and <= 'z' or >= '0' and <= '9'))
            return false;

        foreach (char character in key)
        {
            if (!(character is >= 'a' and <= 'z'
                  or >= '0' and <= '9'
                  or '_' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static SocialGiftPolicyOptions CopyAndValidateOptions(SocialGiftPolicyOptions source)
    {
        if (source.MaximumCandidateCount is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(source), "MaximumCandidateCount 必须在 1 到 64 之间。");

        return new SocialGiftPolicyOptions
        {
            MaximumCandidateCount = source.MaximumCandidateCount,
        };
    }

    private static GiftPolicyService CreateFailedService(
        string issue,
        ISocialGiftItemResolver? itemResolver,
        SocialGiftPolicyOptions? options)
    {
        var service = new GiftPolicyService(
            new SocialGiftPoolCatalog(),
            itemResolver ?? new StardewGiftItemResolver(),
            options);
        service.CatalogIssues = new[] { issue };
        return service;
    }

    private static SocialGiftCandidateSet Blocked(SocialGiftRejectionReason reason)
        => new() { BlockReason = reason };

    private static SocialGiftSelectionResult Rejected(SocialGiftRejectionReason reason)
        => new()
        {
            Kind = SocialGiftSelectionKind.Rejected,
            RejectionReason = reason,
        };

    private static string NormalizeText(string? value, int maximumLength)
    {
        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string SanitizeIssue(string? value)
        => NormalizeText(value, 300);

    private sealed record RankedCandidate(
        SocialGiftCandidate Candidate,
        int NpcSpecific,
        int MatchedTagCount,
        int Priority);
}

/// <summary>Production resolver backed by Stardew Valley 1.6's ItemRegistry.</summary>
public sealed class StardewGiftItemResolver : ISocialGiftItemResolver
{
    private static readonly string[] UnsafeContextTags =
    {
        "quest_item",
        "special_item",
        "unique_item",
        "legendary_item",
        "legendary_fish",
        "fish_legendary",
        "lost_item",
        "currency_item",
        "book_item",
    };

    public bool TryResolve(string qualifiedItemId, out SocialGiftItemFacts? facts)
    {
        facts = null;
        if (string.IsNullOrWhiteSpace(qualifiedItemId)
            || !ItemRegistry.Exists(qualifiedItemId))
        {
            return false;
        }

        Item? item;
        try
        {
            item = ItemRegistry.Create(qualifiedItemId, 1, 0, allowNull: true);
        }
        catch
        {
            return false;
        }
        if (item is null)
            return false;

        bool isWeapon = item is MeleeWeapon
                        || string.Equals(item.TypeDefinitionId, ItemRegistry.type_weapon, StringComparison.Ordinal);
        bool isTool = item is Tool
                      || string.Equals(item.TypeDefinitionId, ItemRegistry.type_tool, StringComparison.Ordinal);
        bool isObject = item is StardewValley.Object
                        && string.Equals(item.TypeDefinitionId, ItemRegistry.type_object, StringComparison.Ordinal);
        bool canBeTrashed = SafeBoolean(item.canBeTrashed);
        bool canBeShipped = SafeBoolean(item.canBeShipped);
        bool canBeGivenAsGift = SafeBoolean(item.canBeGivenAsGift);
        bool hasUnsafeTag = UnsafeContextTags.Any(tag => SafeBoolean(() => item.HasContextTag(tag)));
        bool isQuestOrUnique = item is SpecialItem || hasUnsafeTag;

        facts = new SocialGiftItemFacts
        {
            Exists = true,
            QualifiedItemId = item.QualifiedItemId,
            DisplayName = item.DisplayName,
            TypeDefinitionId = item.TypeDefinitionId,
            SellPrice = SafePrice(() => item.sellToStorePrice(-1)),
            PurchasePrice = SafePrice(() => item.salePrice(ignoreProfitMargins: true)),
            IsObject = isObject,
            IsTool = isTool,
            IsWeapon = isWeapon,
            IsQuestOrUnique = isQuestOrUnique,
            CanBeTrashed = canBeTrashed,
            CanBeShipped = canBeShipped,
            CanBeGivenAsGift = canBeGivenAsGift,
        };
        return true;
    }

    private static bool SafeBoolean(Func<bool> getValue)
    {
        try
        {
            return getValue();
        }
        catch
        {
            return false;
        }
    }

    private static int SafePrice(Func<int> getValue)
    {
        try
        {
            return Math.Max(0, getValue());
        }
        catch
        {
            return 0;
        }
    }
}
