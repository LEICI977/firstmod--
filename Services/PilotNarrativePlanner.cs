namespace VivantValley.Services;

/// <summary>Pure scheduling rules for the first proactive-NPC vertical slice.</summary>
public sealed class PilotNarrativePlanner
{
    public bool CanSchedule(
        NpcNarrativeState state,
        StoryDefinition story,
        long completedConversationTurns,
        int vanillaHearts,
        int currentDay)
    {
        if (story is null)
            throw new ArgumentNullException(nameof(story));

        if (!CanEnterStory(state, story))
            return false;

        return CanSchedule(
            state,
            completedConversationTurns,
            vanillaHearts,
            currentDay,
            story.Trigger.MinConversationTurns,
            story.Trigger.MinHearts,
            story.Trigger.CooldownDays);
    }

    public bool CanEnterStory(NpcNarrativeState state, StoryDefinition story)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));
        if (story is null)
            throw new ArgumentNullException(nameof(story));

        return story.Enabled
               && (story.Repeatable || !state.CompletedStoryIds.Contains(story.Id))
               && story.Trigger.RequiredFlags.IsSubsetOf(state.Flags)
               && !story.Trigger.ForbiddenFlags.Overlaps(state.Flags);
    }

    public bool CanSchedule(
        NpcNarrativeState state,
        long completedConversationTurns,
        int vanillaHearts,
        int currentDay,
        int minimumConversationTurns,
        int minimumHearts,
        int cooldownDays)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        bool hasLiveAction = state.PendingEncounter is not null
                             && state.PendingEncounter.Status is not PlannedEncounterStatus.Completed
                                 and not PlannedEncounterStatus.Expired
                                 and not PlannedEncounterStatus.Cancelled;

        return !hasLiveAction
               && completedConversationTurns >= minimumConversationTurns
               && completedConversationTurns > state.LastConversationTurnScheduled
               && vanillaHearts >= minimumHearts
               && currentDay - state.LastEncounterDay >= cooldownDays;
    }

    public PlannedNpcEncounter CreateEncounter(
        StoryDefinition story,
        long sourceConversationTurn,
        int currentDay,
        string triggerExcerpt,
        bool immediate = false)
    {
        if (story is null)
            throw new ArgumentNullException(nameof(story));

        PlannedNpcEncounter encounter = CreateEncounter(
            story.Npc,
            sourceConversationTurn,
            currentDay,
            story.Trigger.DelayDays,
            story.Trigger.ExpiryDays,
            story.Scene.GiftItemId,
            triggerExcerpt,
            immediate);
        encounter.StoryId = story.Id;
        encounter.StoryVersion = story.Version;
        encounter.Repeatable = story.Repeatable;
        encounter.StartTime = immediate ? 600 : story.Scene.StartTime;
        encounter.EndTime = story.Scene.EndTime;
        encounter.ActivationDistanceTiles = story.Scene.ActivationDistanceTiles;
        encounter.AiBrief = story.Scene.AiBrief;
        encounter.FallbackText = story.Scene.FallbackText;
        encounter.AcceptText = story.Scene.AcceptText;
        encounter.DeferText = story.Scene.DeferText;
        encounter.TrustOnAccept = story.AcceptEffects.Trust;
        encounter.AffectionOnAccept = story.AcceptEffects.Affection;
        encounter.FlagsOnAccept = new HashSet<string>(story.AcceptEffects.SetFlags, StringComparer.Ordinal);
        encounter.Choices = CreatePlannedChoices(story);
        return encounter;
    }

    public static List<PlannedStoryChoice> CreatePlannedChoices(StoryDefinition story)
    {
        if (story is null)
            throw new ArgumentNullException(nameof(story));

        if (story.Choices.Count == 0)
        {
            return new List<PlannedStoryChoice>
            {
                new()
                {
                    Id = "accept",
                    Text = story.Scene.AcceptText,
                    MemoryText = story.Scene.AcceptText,
                    ReceiveGift = !string.IsNullOrWhiteSpace(story.Scene.GiftItemId),
                    Trust = story.AcceptEffects.Trust,
                    Affection = story.AcceptEffects.Affection,
                    SetFlags = new HashSet<string>(story.AcceptEffects.SetFlags, StringComparer.Ordinal),
                },
                new()
                {
                    Id = "defer",
                    Text = story.Scene.DeferText,
                    MemoryText = story.Scene.DeferText,
                    Defer = true,
                },
            };
        }

        return story.Choices.Select(choice => new PlannedStoryChoice
        {
            Id = choice.Id,
            Text = choice.Text,
            MemoryText = string.IsNullOrWhiteSpace(choice.MemoryText) ? choice.Text : choice.MemoryText,
            ReceiveGift = choice.ReceiveGift,
            Defer = choice.Defer,
            NextStoryId = choice.NextStoryId,
            Trust = choice.Effects.Trust,
            Affection = choice.Effects.Affection,
            SetFlags = new HashSet<string>(choice.Effects.SetFlags, StringComparer.Ordinal),
        }).ToList();
    }

    public PlannedNpcEncounter CreateEncounter(
        string npcName,
        long sourceConversationTurn,
        int currentDay,
        int delayDays,
        int expiryDays,
        string giftItemId,
        string triggerExcerpt,
        bool immediate = false)
    {
        int earliestDay = immediate ? currentDay : checked(currentDay + Math.Max(0, delayDays));
        return new PlannedNpcEncounter
        {
            ActionId = Guid.NewGuid().ToString("N"),
            NpcName = npcName.Trim(),
            SourceConversationTurn = sourceConversationTurn,
            EarliestDay = earliestDay,
            ExpiryDay = checked(earliestDay + Math.Max(1, expiryDays) - 1),
            StartTime = immediate ? 600 : 900,
            EndTime = 2600,
            GiftItemId = giftItemId.Trim(),
            ActivationDistanceTiles = 8f,
            TriggerExcerpt = LimitExcerpt(triggerExcerpt),
            Status = PlannedEncounterStatus.Planned,
        };
    }

    public static bool IsReady(PlannedNpcEncounter encounter, int currentDay, int timeOfDay)
    {
        return encounter.Status is PlannedEncounterStatus.Planned or PlannedEncounterStatus.Deferred
               && currentDay >= encounter.EarliestDay
               && currentDay <= encounter.ExpiryDay
               && timeOfDay >= encounter.StartTime
               && timeOfDay <= encounter.EndTime;
    }

    public static bool IsExpired(PlannedNpcEncounter encounter, int currentDay)
    {
        return encounter.Status == PlannedEncounterStatus.Expired
               || (encounter.Status is not PlannedEncounterStatus.Completed and not PlannedEncounterStatus.Cancelled
                   && currentDay > encounter.ExpiryDay);
    }

    public static void Defer(PlannedNpcEncounter encounter, int currentDay, int maximumAttempts = 3)
    {
        encounter.Attempts++;
        if (encounter.Attempts >= Math.Max(1, maximumAttempts))
        {
            encounter.Status = PlannedEncounterStatus.Expired;
            return;
        }

        encounter.EarliestDay = Math.Max(encounter.EarliestDay, checked(currentDay + 1));
        encounter.ExpiryDay = Math.Max(encounter.ExpiryDay, encounter.EarliestDay + 1);
        encounter.Status = PlannedEncounterStatus.Deferred;
    }

    private static string LimitExcerpt(string? value)
    {
        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 240 ? normalized : normalized[..240] + "…";
    }
}
