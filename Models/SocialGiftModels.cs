using System;
using System.Collections.Generic;

namespace VivantValley;

/// <summary>Static, author-reviewed gift definitions loaded from assets/social/gift-pools.json.</summary>
public sealed class SocialGiftPoolCatalog
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Legacy schema-v1 entries.</summary>
    public List<SocialGiftPoolEntry> Gifts { get; set; } = new();

    /// <summary>Schema-v2 reusable, code-validated item definitions.</summary>
    public List<SocialGiftItemTemplate> Items { get; set; } = new();

    /// <summary>Schema-v2 template keys offered to every NPC.</summary>
    public List<string> Global { get; set; } = new();

    /// <summary>Schema-v2 template keys selected independently for each NPC.</summary>
    public Dictionary<string, List<string>> NpcPools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SocialGiftItemTemplate
{
    public string Key { get; set; } = string.Empty;

    public string QualifiedItemId { get; set; } = string.Empty;

    public string DisplayHint { get; set; } = string.Empty;

    public List<string> ApplicableTags { get; set; } = new();

    public string Category { get; set; } = SocialGiftCategories.General;

    public int Priority { get; set; }

    public int MinHearts { get; set; }

    public int RepeatCooldownDays { get; set; } = 7;

    public List<string> DeliveryModes { get; set; } = new()
    {
        SocialGiftDeliveryModes.Immediate,
        SocialGiftDeliveryModes.Mail,
    };

    public bool Enabled { get; set; } = true;
}

/// <summary>A static gift option. The AI may see <see cref="Key"/>, but never supplies an item ID.</summary>
public sealed class SocialGiftPoolEntry
{
    /// <summary>Stable key returned by the AI, such as <c>mining_quartz</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>A qualified object ID such as <c>(O)80</c>.</summary>
    public string QualifiedItemId { get; set; } = string.Empty;

    /// <summary>Short guidance shown to the AI when choosing between safe candidates.</summary>
    public string DisplayHint { get; set; } = string.Empty;

    /// <summary>NPC internal names allowed to offer this item. Empty means any social NPC.</summary>
    public List<string> NpcNames { get; set; } = new();

    /// <summary>
    /// Context tags for which this option is appropriate. The special tag <c>general</c>
    /// is always considered present.
    /// </summary>
    public List<string> ApplicableTags { get; set; } = new();

    /// <summary>Deterministic tie-break priority after NPC and tag relevance.</summary>
    public int Priority { get; set; }

    public string Category { get; set; } = SocialGiftCategories.General;

    public int MinHearts { get; set; }

    public int RepeatCooldownDays { get; set; } = 7;

    public List<string> DeliveryModes { get; set; } = new()
    {
        SocialGiftDeliveryModes.Immediate,
        SocialGiftDeliveryModes.Mail,
    };

    public bool Enabled { get; set; } = true;
}

/// <summary>Limits enforced by code independently from AI output and the JSON catalog.</summary>
public sealed class SocialGiftPolicyOptions
{
    /// <summary>Maximum safe options exposed to the AI for one encounter.</summary>
    public int MaximumCandidateCount { get; set; } = 12;
}

/// <summary>Immutable-at-call-site facts used to decide whether this action may offer a gift.</summary>
public sealed class GiftPolicyContext
{
    public string ActionId { get; init; } = string.Empty;

    public string NpcName { get; init; } = string.Empty;

    public int CurrentDay { get; init; }

    public bool GiftAlreadyOfferedToday { get; init; }

    public IReadOnlyCollection<string> CompletedActionIds { get; init; } = Array.Empty<string>();

    /// <summary>Recent activity/personality tags, for example <c>mining</c> or <c>low_energy</c>.</summary>
    public IReadOnlyCollection<string> RelevantTags { get; init; } = Array.Empty<string>();

    public int HeartLevel { get; init; }

    public string DeliveryMode { get; init; } = SocialGiftDeliveryModes.Immediate;

    public IReadOnlyCollection<NpcGiftHistoryEntry> RecentGifts { get; init; }
        = Array.Empty<NpcGiftHistoryEntry>();
}

/// <summary>Runtime item facts. A fake resolver can supply these without loading Stardew Valley.</summary>
public sealed class SocialGiftItemFacts
{
    public bool Exists { get; init; }

    public string QualifiedItemId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string TypeDefinitionId { get; init; } = string.Empty;

    public int SellPrice { get; init; }

    public int PurchasePrice { get; init; }

    public bool IsObject { get; init; }

    public bool IsTool { get; init; }

    public bool IsWeapon { get; init; }

    public bool IsQuestOrUnique { get; init; }

    public bool CanBeTrashed { get; init; }

    public bool CanBeShipped { get; init; }

    public bool CanBeGivenAsGift { get; init; }

    public int EconomicValue => Math.Max(Math.Max(0, SellPrice), Math.Max(0, PurchasePrice));
}

/// <summary>Resolves game item data. Implement this interface with fixed facts in unit tests.</summary>
public interface ISocialGiftItemResolver
{
    bool TryResolve(string qualifiedItemId, out SocialGiftItemFacts? facts);
}

/// <summary>A fully validated option which is safe to expose to the AI.</summary>
public sealed class SocialGiftCandidate
{
    public string Key { get; init; } = string.Empty;

    public string QualifiedItemId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DisplayHint { get; init; } = string.Empty;

    public IReadOnlyList<string> ApplicableTags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MatchedTags { get; init; } = Array.Empty<string>();

    public int EconomicValue { get; init; }

    /// <summary>Stack size delivered for this candidate. Items below 100g are batched to roughly 100g.</summary>
    public int Quantity { get; init; } = 1;

    public string Category { get; init; } = SocialGiftCategories.General;

    public int MinHearts { get; init; }

    public int RepeatCooldownDays { get; init; }
}

public static class SocialGiftCategories
{
    public const string Signature = "signature";
    public const string Activity = "activity";
    public const string Seasonal = "seasonal";
    public const string Care = "care";
    public const string Fallback = "fallback";
    public const string General = "general";
}

public static class SocialGiftDeliveryModes
{
    public const string Immediate = "immediate";
    public const string Mail = "mail";
}

public enum SocialGiftRejectionReason
{
    None,
    InvalidContext,
    EmptySelection,
    DuplicateActionId,
    NpcAlreadyOfferedToday,
    CatalogUnavailable,
    NoApplicableCandidates,
    UnknownCandidateKey,
    UnknownItem,
    NonObjectItem,
    ToolOrWeapon,
    QuestOrUniqueItem,
    ItemCannotBeTrashed,
    ItemCannotBeShipped,
    ItemCannotBeGivenAsGift,
    InvalidItemValue,
    RelationshipTooLow,
    DeliveryModeNotAllowed,
    RecentlyGiven,
}

public sealed class SocialGiftCandidateRejection
{
    public string Key { get; init; } = string.Empty;

    public SocialGiftRejectionReason Reason { get; init; }
}

/// <summary>The deterministic allowlist generated for one encounter.</summary>
public sealed class SocialGiftCandidateSet
{
    public IReadOnlyList<SocialGiftCandidate> Candidates { get; init; } = Array.Empty<SocialGiftCandidate>();

    public IReadOnlyList<SocialGiftCandidateRejection> Rejections { get; init; }
        = Array.Empty<SocialGiftCandidateRejection>();

    public SocialGiftRejectionReason BlockReason { get; init; }

    public bool CanOfferGift => BlockReason == SocialGiftRejectionReason.None && Candidates.Count > 0;
}

public enum SocialGiftSelectionKind
{
    Rejected,
    TalkOnly,
    Gift,
}

/// <summary>Result of revalidating the AI's selected key against current state.</summary>
public sealed class SocialGiftSelectionResult
{
    public SocialGiftSelectionKind Kind { get; init; }

    public SocialGiftRejectionReason RejectionReason { get; init; }

    public SocialGiftCandidate? Candidate { get; init; }

    public bool IsApproved => Kind is SocialGiftSelectionKind.TalkOnly or SocialGiftSelectionKind.Gift;
}

public static class NpcGiftToolNames
{
    public const string None = "none";
    public const string GiveGift = "give_gift";
    public const string MailGift = "mail_gift";
}

public sealed class AiGiftToolRequest
{
    public string NpcName { get; init; } = string.Empty;

    public string NpcDisplayName { get; init; } = string.Empty;

    public string GameContext { get; init; } = string.Empty;

    public string PlayerMessage { get; init; } = string.Empty;

    /// <summary>Legacy field retained for callers from 0.7; planning no longer inspects a generated reply.</summary>
    public string NpcReply { get; init; } = string.Empty;

    public string RecentConversation { get; init; } = string.Empty;

    public string ActivitySummary { get; init; } = string.Empty;

    public IReadOnlyList<SocialSceneGiftOption> GiftCandidates { get; init; }
        = Array.Empty<SocialSceneGiftOption>();

    public string Model { get; init; } = "deepseek-v4-flash";

    public string ThinkingType { get; init; } = "disabled";

    public string ReasoningEffort { get; init; } = "low";

    public int MaxOutputTokens { get; init; } = 256;
}

public sealed class AiGiftToolDecision
{
    public string ToolName { get; init; } = NpcGiftToolNames.None;

    public string? GiftCandidateId { get; init; }

    public string ReasonTag { get; init; } = string.Empty;

    public bool UsedFallback { get; init; }

    public string FailureReason { get; init; } = string.Empty;

    public bool ShouldGiveGift => ToolName.Equals(NpcGiftToolNames.GiveGift, StringComparison.Ordinal);

    public bool ShouldMailGift => ToolName.Equals(NpcGiftToolNames.MailGift, StringComparison.Ordinal);

    public bool ShouldUseGiftTool => ShouldGiveGift || ShouldMailGift;
}
