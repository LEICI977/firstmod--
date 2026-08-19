using System;
using System.Collections.Generic;

namespace VivantValley;

/// <summary>An authored proactive story node loaded from assets/stories.</summary>
public sealed class StoryDefinition
{
    public string Id { get; set; } = string.Empty;

    public int Version { get; set; } = 1;

    public string Npc { get; set; } = string.Empty;

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public bool Repeatable { get; set; }

    public StoryTriggerDefinition Trigger { get; set; } = new();

    public StorySceneDefinition Scene { get; set; } = new();

    public List<StoryChoiceDefinition> Choices { get; set; } = new();

    /// <summary>Legacy two-button effect used only when an older story omits choices.</summary>
    public StoryEffectsDefinition AcceptEffects { get; set; } = new();
}

public sealed class StoryTriggerDefinition
{
    public int MinHearts { get; set; }

    public int MinConversationTurns { get; set; } = 1;

    public int DelayDays { get; set; } = 1;

    public int ExpiryDays { get; set; } = 5;

    public int CooldownDays { get; set; } = 5;

    public HashSet<string> RequiredFlags { get; set; } = new(StringComparer.Ordinal);

    public HashSet<string> ForbiddenFlags { get; set; } = new(StringComparer.Ordinal);
}

public sealed class StorySceneDefinition
{
    public int StartTime { get; set; } = 900;

    public int EndTime { get; set; } = 2200;

    public float ActivationDistanceTiles { get; set; } = 8f;

    public string GiftItemId { get; set; } = string.Empty;

    public string AiBrief { get; set; } = string.Empty;

    public string FallbackText { get; set; } = string.Empty;

    public string AcceptText { get; set; } = "收下{GiftDisplayName}";

    public string DeferText { get; set; } = "改天再说";
}

public sealed class StoryEffectsDefinition
{
    public int Trust { get; set; }

    public int Affection { get; set; }

    public HashSet<string> SetFlags { get; set; } = new(StringComparer.Ordinal);
}

public sealed class StoryChoiceDefinition
{
    public string Id { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string MemoryText { get; set; } = string.Empty;

    public bool ReceiveGift { get; set; }

    public bool Defer { get; set; }

    public string NextStoryId { get; set; } = string.Empty;

    public StoryEffectsDefinition Effects { get; set; } = new();
}
