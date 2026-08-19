namespace VivantValley.Services;

/// <summary>Applies one already-authorized story choice without game or inventory dependencies.</summary>
public sealed class NarrativeChoiceResolver
{
    public bool TryApply(
        NpcNarrativeState narrative,
        PlannedNpcEncounter encounter,
        PlannedStoryChoice choice,
        int currentDay,
        bool giftDelivered)
    {
        if (narrative is null)
            throw new ArgumentNullException(nameof(narrative));
        if (encounter is null)
            throw new ArgumentNullException(nameof(encounter));
        if (choice is null)
            throw new ArgumentNullException(nameof(choice));

        if (choice.Defer
            || (choice.ReceiveGift && !giftDelivered)
            || narrative.CompletedActionIds.Contains(encounter.ActionId)
            || encounter.Status is PlannedEncounterStatus.Completed
                or PlannedEncounterStatus.Expired
                or PlannedEncounterStatus.Cancelled)
        {
            return false;
        }

        narrative.CompletedActionIds.Add(encounter.ActionId);
        encounter.Status = PlannedEncounterStatus.Completed;
        narrative.LastEncounterDay = currentDay;
        if (giftDelivered)
            narrative.LastGiftDay = currentDay;
        narrative.Trust = Math.Clamp(narrative.Trust + choice.Trust, 0, 100);
        narrative.Affection = Math.Clamp(narrative.Affection + choice.Affection, 0, 100);
        if (!string.IsNullOrWhiteSpace(encounter.StoryId))
            narrative.CompletedStoryIds.Add(encounter.StoryId);
        foreach (string flag in choice.SetFlags)
            narrative.Flags.Add(flag);
        return true;
    }
}
