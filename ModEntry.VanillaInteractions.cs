using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using VivantValley.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using SObject = StardewValley.Object;

namespace VivantValley;

public sealed partial class ModEntry
{
    private readonly PerScreen<VanillaInteractionScreenState> vanillaInteractionStates =
        new(() => new VanillaInteractionScreenState());

    private void TrackVanillaInteractions()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        RefreshVanillaEventLifecycle();
        VanillaInteractionScreenState state = vanillaInteractionStates.Value;
        if (Game1.activeClickableMenu is not DialogueBox box)
        {
            state.ActiveDialogueBox = null;
            state.LastPageFingerprint = string.Empty;
            return;
        }

        if (!ReferenceEquals(state.ActiveDialogueBox, box))
        {
            state.ActiveDialogueBox = box;
            state.LastPageFingerprint = string.Empty;
        }

        // Waiting until the opening transition ends guarantees the player actually saw this page.
        if (box.transitioning)
            return;

        string text = CleanInteractionText(box.getCurrentString());
        if (text.Length == 0)
            return;

        Dialogue? dialogue = box.characterDialogue;
        string translationKey = dialogue?.TranslationKey ?? string.Empty;
        if (translationKey.Equals(GeneratedDialogueKey, StringComparison.Ordinal))
            return;

        NPC? speaker = dialogue?.speaker ?? Game1.currentSpeaker ?? Game1.objectDialoguePortraitPerson;
        string speakerName = speaker?.Name ?? string.Empty;
        string speakerDisplayName = speaker?.displayName ?? speakerName;
        if (speaker is not null)
            text = StripSpeakerPrefix(text, speaker);

        Event? currentEvent = Game1.CurrentEvent;
        string eventId = GetPersistedEventId(currentEvent);
        string locationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
        int dialogueIndex = dialogue?.currentDialogueIndex ?? -1;
        int brokenPageCount = box.characterDialoguesBrokenUp?.Count ?? 0;
        string pageFingerprint = CreateDedupeKey(
            "page",
            Game1.Date.TotalDays.ToString(),
            locationName,
            eventId,
            speakerName,
            translationKey,
            dialogueIndex.ToString(),
            brokenPageCount.ToString(),
            box.isQuestion.ToString(),
            text);
        if (state.LastPageFingerprint.Equals(pageFingerprint, StringComparison.Ordinal))
            return;
        state.LastPageFingerprint = pageFingerprint;

        if (currentEvent is not null)
        {
            if (speaker is not null)
                state.LastEventSpeakerName = speakerName;

            string relatedNpc = speakerName;
            if (relatedNpc.Length == 0 && box.isQuestion)
                relatedNpc = state.LastEventSpeakerName;
            if (relatedNpc.Length == 0 && !box.isQuestion)
                return;

            AddVanillaEventBeat(new NarrativeBeat
            {
                Kind = box.isQuestion ? NarrativeBeatKinds.Question : NarrativeBeatKinds.NpcDialogue,
                NpcName = relatedNpc,
                SpeakerName = speakerName,
                SpeakerDisplayName = speakerDisplayName,
                Text = text,
                TranslationKey = translationKey,
                DedupeKey = pageFingerprint,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        if (speaker is null)
            return;

        AppendVanillaMemoryMessage(
            speaker.Name,
            new ConversationMemoryMessage
            {
                Role = "assistant",
                Content = text,
                GameDate = CurrentInteractionDate(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Source = ConversationMemorySources.VanillaDialogue,
                LocationName = locationName,
                TranslationKey = translationKey,
                DedupeKey = pageFingerprint,
            });
    }

    private void TrackVanillaMenuChanged(MenuChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        VanillaInteractionScreenState state = vanillaInteractionStates.Value;
        if (!ReferenceEquals(e.OldMenu, e.NewMenu))
        {
            state.ActiveDialogueBox = e.NewMenu as DialogueBox;
            state.LastPageFingerprint = string.Empty;
        }

        RefreshVanillaEventLifecycle();
    }

    internal void RecordVanillaGift(NPC npc, SObject item, Farmer giver)
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || npc is null
            || item is null
            || giver is null
            || !giver.IsLocalPlayer
            || string.IsNullOrWhiteSpace(npc.Name))
        {
            return;
        }

        int? taste = null;
        try
        {
            taste = npc.getGiftTasteForThisItem(item);
        }
        catch (Exception ex)
        {
            Monitor.Log($"无法读取 {npc.Name} 对 {item.QualifiedItemId} 的礼物偏好：{ex.Message}", LogLevel.Debug);
        }

        string locationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
        string giftKey = CreateDedupeKey(
            "gift",
            Game1.Date.TotalDays.ToString(),
            Game1.timeOfDay.ToString(),
            locationName,
            GetPersistedEventId(Game1.CurrentEvent),
            npc.Name,
            item.QualifiedItemId,
            item.DisplayName);
        if (Game1.CurrentEvent is not null)
        {
            AddVanillaEventBeat(new NarrativeBeat
            {
                Kind = NarrativeBeatKinds.Gift,
                NpcName = npc.Name,
                SpeakerName = npc.Name,
                SpeakerDisplayName = npc.displayName,
                ItemId = item.QualifiedItemId,
                ItemName = item.DisplayName,
                Quantity = 1,
                GiftTaste = taste,
                DedupeKey = giftKey,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        string tasteText = taste.HasValue
            ? $"；你的实际偏好反应是{NarrativeContextService.DescribeGiftTaste(taste.Value)}"
            : string.Empty;
        AppendVanillaMemoryMessage(
            npc.Name,
            new ConversationMemoryMessage
            {
                Role = "system",
                Content = $"[原版送礼事实] 玩家当面送给你 {item.DisplayName} x1{tasteText}。",
                GameDate = CurrentInteractionDate(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Source = ConversationMemorySources.VanillaGift,
                LocationName = locationName,
                DedupeKey = giftKey,
            });
    }

    internal void RecordVanillaDialogueChoice(Dialogue dialogue, Response response)
    {
        if (!CanRecordPatchedInteraction() || dialogue is null || response is null)
            return;
        if ((dialogue.TranslationKey ?? string.Empty).Equals(GeneratedDialogueKey, StringComparison.Ordinal))
            return;

        NPC? speaker = dialogue.speaker ?? Game1.currentSpeaker;
        RecordVanillaChoice(
            speaker?.Name ?? string.Empty,
            speaker?.displayName ?? string.Empty,
            response.responseText,
            response.responseKey,
            "dialogue_choice");
    }

    internal void RecordVanillaLocationChoice(GameLocation location, Response response)
    {
        if (!CanRecordPatchedInteraction() || location is null || response is null)
            return;

        NPC? speaker = Game1.objectDialoguePortraitPerson ?? Game1.currentSpeaker;
        string npcName = speaker?.Name ?? vanillaInteractionStates.Value.LastEventSpeakerName;
        RecordVanillaChoice(
            npcName,
            speaker?.displayName ?? npcName,
            response.responseText,
            response.responseKey,
            "location_choice:" + (location.lastQuestionKey ?? string.Empty));
    }

    internal void RecordVanillaEventChoice(Event currentEvent, string questionKey, int answerChoice)
    {
        if (!CanRecordPatchedInteraction() || currentEvent is null)
            return;

        string responseText = string.Empty;
        string responseKey = answerChoice.ToString();
        if (Game1.activeClickableMenu is DialogueBox box
            && box.responses is not null
            && answerChoice >= 0
            && answerChoice < box.responses.Length)
        {
            responseText = box.responses[answerChoice].responseText;
            responseKey = box.responses[answerChoice].responseKey ?? responseKey;
        }

        RecordVanillaChoice(
            vanillaInteractionStates.Value.LastEventSpeakerName,
            string.Empty,
            responseText.Length == 0 ? responseKey : responseText,
            responseKey,
            "event_choice:" + (questionKey ?? string.Empty));
    }

    private void RecordVanillaChoice(
        string npcName,
        string npcDisplayName,
        string? responseText,
        string? responseKey,
        string choiceContext)
    {
        string text = CleanInteractionText(responseText);
        if (text.Length == 0)
            text = CleanInteractionText(responseKey);
        if (text.Length == 0)
            return;

        string locationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty;
        string eventId = GetPersistedEventId(Game1.CurrentEvent);
        string choiceKey = CreateDedupeKey(
            "choice",
            Game1.Date.TotalDays.ToString(),
            locationName,
            eventId,
            npcName,
            choiceContext,
            responseKey ?? string.Empty,
            text);

        if (Game1.CurrentEvent is not null)
        {
            AddVanillaEventBeat(new NarrativeBeat
            {
                Kind = NarrativeBeatKinds.PlayerChoice,
                NpcName = npcName,
                SpeakerName = Game1.player.Name,
                SpeakerDisplayName = Game1.player.displayName,
                Text = text,
                DedupeKey = choiceKey,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            return;
        }

        if (string.IsNullOrWhiteSpace(npcName))
            return;

        AppendVanillaMemoryMessage(
            npcName,
            new ConversationMemoryMessage
            {
                Role = "user",
                Content = $"[原版互动选择] {text}",
                GameDate = CurrentInteractionDate(),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Source = ConversationMemorySources.VanillaChoice,
                LocationName = locationName,
                DedupeKey = choiceKey,
            });
    }

    private void AppendVanillaMemoryMessage(string npcName, ConversationMemoryMessage message)
    {
        if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(message.Content))
            return;

        NpcConversationMemory memory = memoryStore.GetOrCreate(GetPlayerId(), npcName);
        if (message.DedupeKey.Length > 0
            && memory.Messages.Any(existing => string.Equals(
                existing.DedupeKey,
                message.DedupeKey,
                StringComparison.Ordinal)))
        {
            return;
        }

        memory.Messages.Add(message);
        memory.Messages = ConversationMemoryPolicy.KeepRecentConversationTurns(memory.Messages);
        memory.LastDate = message.GameDate;
        memoryDirty = true;
        PersistMemory(force: false);
        Monitor.Log($"已立即记录 {npcName} 的原版互动：source={message.Source}。", LogLevel.Debug);
    }

    private void AddVanillaEventBeat(NarrativeBeat beat)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || Game1.CurrentEvent is null)
            return;

        NarrativeEpisode episode = GetOrCreateActiveVanillaEpisode(Game1.CurrentEvent);
        if (beat.DedupeKey.Length > 0
            && episode.Beats.Any(existing => string.Equals(
                existing.DedupeKey,
                beat.DedupeKey,
                StringComparison.Ordinal)))
        {
            return;
        }

        beat.Sequence = episode.Beats.Count == 0 ? 1 : episode.Beats.Max(existing => existing.Sequence) + 1;
        beat.Normalize();
        episode.Beats.Add(beat);
        if (!string.IsNullOrWhiteSpace(beat.NpcName)
            && !episode.ParticipantNames.Contains(beat.NpcName, StringComparer.OrdinalIgnoreCase))
        {
            episode.ParticipantNames.Add(beat.NpcName);
        }
        if (!string.IsNullOrWhiteSpace(beat.SpeakerName)
            && !beat.SpeakerName.Equals(Game1.player.Name, StringComparison.OrdinalIgnoreCase)
            && !episode.ParticipantNames.Contains(beat.SpeakerName, StringComparer.OrdinalIgnoreCase))
        {
            episode.ParticipantNames.Add(beat.SpeakerName);
        }

        episode.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
        memoryDirty = true;
        PersistMemory(force: false);
        Monitor.Log(
            $"已立即记录原版事件 {episode.EventId}：kind={beat.Kind}，NPC={beat.NpcName}，seq={beat.Sequence}。",
            LogLevel.Debug);
    }

    private NarrativeEpisode GetOrCreateActiveVanillaEpisode(Event currentEvent)
    {
        RefreshVanillaEventLifecycle();
        VanillaInteractionScreenState state = vanillaInteractionStates.Value;
        List<NarrativeEpisode> episodes = memoryStore.GetNarrativeEpisodes(GetPlayerId());
        if (state.ActiveEpisodeId.Length > 0)
        {
            NarrativeEpisode? active = episodes.FirstOrDefault(episode =>
                episode.EpisodeId.Equals(state.ActiveEpisodeId, StringComparison.Ordinal));
            if (active is not null)
                return active;
        }

        string eventId = GetPersistedEventId(currentEvent);
        NarrativeEpisode? resumable = episodes.LastOrDefault(episode =>
            !episode.IsCompleted
            && episode.TotalDays == Game1.Date.TotalDays
            && episode.EventId.Equals(eventId, StringComparison.Ordinal));
        if (resumable is not null)
        {
            state.ActiveEpisodeId = resumable.EpisodeId;
            return resumable;
        }

        int occurrence = episodes.Count(episode =>
            episode.TotalDays == Game1.Date.TotalDays
            && episode.EventId.Equals(eventId, StringComparison.Ordinal)) + 1;
        var created = new NarrativeEpisode
        {
            EpisodeId = CreateEpisodeId(Game1.Date.TotalDays, eventId, occurrence),
            EventId = eventId,
            GameDate = Game1.Date.ToString(),
            TotalDays = Game1.Date.TotalDays,
            StartedTimeOfDay = Game1.timeOfDay,
            LocationName = Game1.currentLocation?.NameOrUniqueName ?? string.Empty,
            StartedAtUtc = DateTimeOffset.UtcNow,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        episodes.Add(created);
        state.ActiveEpisodeId = created.EpisodeId;
        memoryDirty = true;
        return created;
    }

    private void RefreshVanillaEventLifecycle()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        VanillaInteractionScreenState state = vanillaInteractionStates.Value;
        string currentRuntimeKey = GetRuntimeEventKey(Game1.CurrentEvent);
        if (state.ActiveEventRuntimeKey.Length > 0
            && !state.ActiveEventRuntimeKey.Equals(currentRuntimeKey, StringComparison.Ordinal))
        {
            CompleteActiveVanillaEvent("event_changed");
        }

        if (currentRuntimeKey.Length > 0)
            state.ActiveEventRuntimeKey = currentRuntimeKey;
    }

    private void CompleteActiveVanillaEvent(string reason)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        VanillaInteractionScreenState state = vanillaInteractionStates.Value;
        if (state.ActiveEpisodeId.Length > 0)
        {
            NarrativeEpisode? episode = memoryStore.GetNarrativeEpisodes(GetPlayerId()).FirstOrDefault(candidate =>
                candidate.EpisodeId.Equals(state.ActiveEpisodeId, StringComparison.Ordinal));
            if (episode is not null && !episode.IsCompleted)
            {
                episode.IsCompleted = true;
                episode.CompletedTimeOfDay = Game1.timeOfDay;
                episode.CompletedAtUtc = DateTimeOffset.UtcNow;
                episode.LastUpdatedAtUtc = DateTimeOffset.UtcNow;
                memoryDirty = true;
                PersistMemory(force: false);
                Monitor.Log($"原版事件记忆已完整封存：{episode.EventId}，原因={reason}。", LogLevel.Debug);
            }
        }

        state.ActiveEventRuntimeKey = string.Empty;
        state.ActiveEpisodeId = string.Empty;
        state.LastEventSpeakerName = string.Empty;
        state.LastPageFingerprint = string.Empty;
    }

    private void RepairStaleVanillaEpisodesAfterLoad()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        bool changed = false;
        foreach (NarrativeEpisode episode in memoryStore.GetNarrativeEpisodes(GetPlayerId()).Where(episode => !episode.IsCompleted))
        {
            episode.IsCompleted = true;
            episode.CompletedTimeOfDay = episode.CompletedTimeOfDay > 0
                ? episode.CompletedTimeOfDay
                : episode.StartedTimeOfDay;
            episode.CompletedAtUtc ??= episode.LastUpdatedAtUtc;
            changed = true;
        }

        if (changed)
            memoryDirty = true;
    }

    private NpcGameSnapshot BuildNpcGameSnapshot(NPC npc, string? playerId = null)
    {
        NpcGameSnapshot snapshot = contextBuilder.Build(npc);
        string[] activeActivities = new[]
        {
            npcMoveToolService.GetActivitySummary(npc.Name),
            npcMineGuardService.GetActivitySummary(npc.Name),
            npcFishingService.GetActivitySummary(npc.Name),
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Cast<string>()
        .ToArray();
        string sceneSnapshot = snapshot.SceneSnapshot;
        string activitySupplement = string.Empty;
        if (activeActivities.Length > 0)
        {
            string activityText = string.Join("；", activeActivities);
            sceneSnapshot = sceneSnapshot.TrimEnd() + "\n- 当前特殊活动：" + activityText;
            activitySupplement = "\n\n【NPC 实时活动补充】\n- 当前特殊活动：" + activityText;
        }
        string resolvedPlayerId = playerId ?? GetPlayerId();
        NpcConversationMemory? conversationMemory = memoryStore.TryGet(
            resolvedPlayerId,
            npc.Name,
            out NpcConversationMemory? existingMemory)
            ? existingMemory
            : null;
        IReadOnlyList<string> sessionFacts = conversationSessionMemory.BuildPromptFacts(
            resolvedPlayerId,
            npc.Name,
            conversationMemory?.TotalTurns ?? 0);
        string narrativeContext = narrativeContextService.Build(
            memoryStore.GetNarrativeEpisodes(resolvedPlayerId),
            npc.Name,
            config.MaxCompleteNarrativeEpisodesInContext,
            config.MaxNarrativeEpisodeAnchorsInContext,
            config.MaxNarrativeContextCharacters);

        return snapshot with
        {
            SystemPrompt = (narrativeContext.Length == 0
                ? snapshot.SystemPrompt
                : snapshot.SystemPrompt.TrimEnd() + "\n\n" + narrativeContext)
                + activitySupplement,
            NarrativeContext = narrativeContext,
            RecentSessionFacts = sessionFacts,
            SceneSnapshot = sceneSnapshot,
        };
    }

    private void ResetVanillaInteractionTracking()
    {
        vanillaInteractionStates.ResetAllScreens();
        if (Context.IsWorldReady && Context.IsMainPlayer)
            RepairStaleVanillaEpisodesAfterLoad();
    }

    private void ResetVanillaDialoguePageTracking()
    {
        VanillaInteractionScreenState state = vanillaInteractionStates.Value;
        state.ActiveDialogueBox = null;
        state.LastPageFingerprint = string.Empty;
    }

    private static bool CanRecordPatchedInteraction()
        => Context.IsWorldReady && Context.IsMainPlayer;

    private static string CurrentInteractionDate()
        => $"{Game1.Date} {Game1.timeOfDay}";

    private static string GetPersistedEventId(Event? currentEvent)
    {
        if (currentEvent is null)
            return string.Empty;
        string id = (currentEvent.id ?? string.Empty).Trim();
        return id.Length == 0 || id.Equals("-1", StringComparison.Ordinal)
            ? "generated_event"
            : id;
    }

    private static string GetRuntimeEventKey(Event? currentEvent)
        => currentEvent is null
            ? string.Empty
            : GetPersistedEventId(currentEvent) + "@" + RuntimeHelpers.GetHashCode(currentEvent);

    private static string CreateEpisodeId(int day, string eventId, int occurrence)
        => $"vanilla-event:{day}:{CreateDedupeKey(eventId)[..16]}:{occurrence}";

    private static string CreateDedupeKey(params string?[] parts)
    {
        string joined = string.Join("\u001f", parts.Select(part => part ?? string.Empty));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
    }

    private static string CleanInteractionText(string? value)
        => string.Join(
            " ",
            (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string StripSpeakerPrefix(string text, NPC speaker)
    {
        string[] prefixes =
        {
            speaker.getName() + ": ",
            speaker.displayName + ": ",
        };
        foreach (string prefix in prefixes.Distinct(StringComparer.Ordinal))
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return text[prefix.Length..].TrimStart();
        }
        return text;
    }

    private sealed class VanillaInteractionScreenState
    {
        public DialogueBox? ActiveDialogueBox { get; set; }

        public string LastPageFingerprint { get; set; } = string.Empty;

        public string LastEventSpeakerName { get; set; } = string.Empty;

        public string ActiveEventRuntimeKey { get; set; } = string.Empty;

        public string ActiveEpisodeId { get; set; } = string.Empty;
    }
}
