using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using VivantValley.Menus;
using VivantValley.Services;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;

namespace VivantValley;

public sealed partial class ModEntry
{
    private const string SocialDirectorSaveDataKey = "npc-social-director-v1";
    private const int SocialPlannerVersion = 2;
    private const int SocialMorningStartTime = 600;
    private const int SocialEveningStartTime = 1800;
    private const int SocialEncounterCutoffTime = 2400;
    private const string GiftMailAssetName = "Data/mail";

    private readonly DailySocialPlanner dailySocialPlanner = new();
    private readonly List<PendingSignalAnalysis> pendingSignalAnalyses = new();
    private SocialDirectorSaveStore socialStore = new();
    private ConversationSignalExtractor conversationSignalExtractor = null!;
    private PlayerActivityJournal activityJournal = null!;
    private GiftPolicyService giftPolicyService = null!;
    private NpcGiftToolService npcGiftToolService = null!;
    private AiSocialSceneService aiSocialSceneService = null!;
    private OvernightMailPlannerService overnightMailPlannerService = null!;
    private CancellationTokenSource signalAnalysisCancellation = new();
    private CancellationTokenSource overnightMailCancellation = new();
    private Task<OvernightMailPlanDecision>? pendingOvernightMailTask;
    private string pendingOvernightMailTaskPlanId = string.Empty;
    private string overnightMailAttemptedPlanId = string.Empty;
    private int overnightMailDeliveryRetryTicks;
    private int overnightMailDeliveryRetryCount;
    private int overnightMailDeliveryReadyDay = -1;
    private bool giftMailIntegrityCheckPending;
    private int giftMailIntegrityRetryTicks;
    private int giftMailIntegrityRetryCount;
    private readonly PerScreen<string> openGiftMailIds = new(() => string.Empty);
    private bool socialDirty;

    private void InitializeSocialDirector(IModHelper helper)
    {
        conversationSignalExtractor = new ConversationSignalExtractor(deepSeekClient);
        activityJournal = new PlayerActivityJournal(config.ActivityRetentionDays);
        aiSocialSceneService = new AiSocialSceneService(deepSeekClient);
        giftPolicyService = GiftPolicyService.LoadFromFile(
            Path.Combine(helper.DirectoryPath, "assets", "social", "gift-pools.json"),
            options: new SocialGiftPolicyOptions
            {
                MaximumCandidateCount = 12,
            });
        npcGiftToolService = new NpcGiftToolService(deepSeekClient, giftPolicyService);
        overnightMailPlannerService = new OvernightMailPlannerService(deepSeekClient);

        foreach (string issue in giftPolicyService.CatalogIssues)
            Monitor.Log($"社交礼物目录：{issue}", LogLevel.Warn);
        Monitor.Log(
            $"社交导演已启用：每日候选 {config.DailyCandidateMin}-{config.DailyCandidateMax} 名，"
            + "每人早晚各一次机会；每名 NPC 每日最多提出一份礼物，午夜后停止触发。",
            LogLevel.Info);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        conversationSessionMemory.Clear();
        npcCombatStateService.OnDayStarted();
        PersistNpcCombatState();
        FinishCompletedSignalAnalyses();
        EnsureTodaySocialPlan(persistImmediately: true);
        overnightMailAttemptedPlanId = string.Empty;
        TryStartOvernightMailPlan();
        RequestGiftMailIntegrityCheck();
        RecordDayContextTags();
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        if (activityJournal.Trim(player, Game1.Date.TotalDays))
            socialDirty = true;
        if (CleanupReceivedGiftMails(player))
        {
            socialDirty = true;
            InvalidateGiftMailAsset();
        }
        PersistSocial(force: false);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || !e.IsLocalPlayer)
            return;

        if (activityJournal.RecordInventoryChanged(
                socialStore.GetOrCreatePlayer(GetPlayerId()),
                Game1.Date.TotalDays,
                e))
        {
            socialDirty = true;
        }
    }

    private void OnLevelChanged(object? sender, LevelChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || !e.IsLocalPlayer)
            return;

        if (activityJournal.RecordLevelChanged(
                socialStore.GetOrCreatePlayer(GetPlayerId()),
                Game1.Date.TotalDays,
                e))
        {
            socialDirty = true;
        }
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        if (activityJournal.RecordTimeChanged(
                socialStore.GetOrCreatePlayer(GetPlayerId()),
                Game1.Date.TotalDays,
                e))
        {
            socialDirty = true;
        }

        ExpirePassedSocialWindows();
        ConversationScreenState state = screenStates.Value;
        DailySocialTimeSlot? pendingTimeSlot = state.PendingSocialInfo?.TimeSlot
                                               ?? state.QueuedSocialScene?.TimeSlot
                                               ?? state.ActiveSocialScene?.TimeSlot;
        if (pendingTimeSlot is not null && !IsSocialTimeSlotActive(pendingTimeSlot.Value))
            CancelPendingSocialScene(state, retryToday: false);
    }

    private void RecordSocialWarp(WarpedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || !e.IsLocalPlayer)
            return;

        if (activityJournal.RecordWarped(
                socialStore.GetOrCreatePlayer(GetPlayerId()),
                Game1.Date.TotalDays,
                e))
        {
            socialDirty = true;
        }
    }

    private void RecordDayContextTags()
    {
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        DailyActivitySummary today = GetOrCreateActivityDay(player, Game1.Date.TotalDays);
        today.Add("season:" + Game1.Date.SeasonKey.ToLowerInvariant());
        if (Game1.currentLocation.IsRainingHere())
            today.Add("weather:rain");
        else if (Game1.currentLocation.IsSnowingHere())
            today.Add("weather:snow");
        else
            today.Add("weather:clear");
        socialDirty = true;
    }

    private static DailyActivitySummary GetOrCreateActivityDay(PlayerSocialDirectorState player, int day)
    {
        player.ActivityJournal ??= new List<DailyActivitySummary>();
        DailyActivitySummary? summary = player.ActivityJournal.FirstOrDefault(value => value.Day == day);
        if (summary is null)
        {
            summary = new DailyActivitySummary { Day = day };
            player.ActivityJournal.Add(summary);
        }

        return summary;
    }

    private void EnsureTodaySocialPlan(bool persistImmediately)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        int day = Game1.Date.TotalDays;
        string playerId = GetPlayerId();
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(playerId);
        if (DailySocialPlanner.IsCurrentPlan(player.TodayPlan, day, SocialPlannerVersion))
            return;
        if (pendingSignalAnalyses.Any(pending => !pending.Task.IsCompleted))
        {
            Monitor.Log("正在等待上一天的对话信号分析完成，今日社交名单将稍后固定。", LogLevel.Debug);
            return;
        }

        var planningCandidates = new List<SocialPlanningCandidate>();
        if (config.EnableSocialDirector)
        {
            foreach (NpcSocialState npcState in player.NpcStates.Values)
            {
                NPC? npc = Game1.getCharacterFromName(
                    npcState.NpcName,
                    mustBeVillager: false,
                    includeEventActors: false);
                planningCandidates.Add(SocialPlanningCandidate.FromState(
                    npcState,
                    npc is null ? 0 : Game1.player.getFriendshipHeartLevelForNPC(npc.Name),
                    existsInSave: npc is not null,
                    canSocialize: npc is not null && npc.CanSocialize && !npc.IsInvisible));
            }
        }

        string saveId = Game1.uniqueIDForThisGame.ToString(CultureInfo.InvariantCulture);
        player.TodayPlan = dailySocialPlanner.CreatePlan(
            saveId,
            playerId,
            day,
            planningCandidates,
            new DailySocialPlannerOptions
            {
                PlannerVersion = SocialPlannerVersion,
                MinimumCandidates = config.DailyCandidateMin,
                MaximumCandidates = config.DailyCandidateMax,
                ConversationLookbackDays = config.ConversationLookbackDays,
                MinimumPositiveScore = config.PositiveConversationThreshold,
            });
        socialDirty = true;

        Monitor.Log(
            player.TodayPlan.Candidates.Count == 0
                ? "今天没有满足近期积极对话条件的主动相遇候选。"
                : $"今天的社交导演名单已固定：{string.Join(", ", player.TodayPlan.Candidates.Select(candidate => candidate.NpcName).Distinct(StringComparer.OrdinalIgnoreCase))}。",
            LogLevel.Debug);
        if (persistImmediately)
            PersistSocial(force: true);
    }

    private DailySocialPlan? GetCurrentSocialPlan()
    {
        if (!Context.IsWorldReady
            || !socialStore.TryGetPlayer(GetPlayerId(), out PlayerSocialDirectorState? player)
            || player?.TodayPlan is null
            || !DailySocialPlanner.IsCurrentPlan(
                player.TodayPlan,
                Game1.Date.TotalDays,
                SocialPlannerVersion))
        {
            return null;
        }

        return player.TodayPlan;
    }

    private void TryStartSocialEncounter(ConversationScreenState screenState)
    {
        if (!Context.IsMainPlayer
            || !config.EnableSocialDirector
            || Game1.isFestival()
            || !TryGetActiveSocialTimeSlot(out DailySocialTimeSlot activeTimeSlot)
            || screenState.PendingSocialScene is not null
            || screenState.QueuedSocialScene is not null
            || screenState.ActiveSocialScene is not null
            || screenState.SocialMenu is not null
            || !CanOpenOwnMenu())
        {
            return;
        }

        ExpirePassedSocialWindows();
        DailySocialPlan? plan = GetCurrentSocialPlan();
        if (plan is null)
            return;

        string playerId = GetPlayerId();
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(playerId);
        float maximumDistanceSquared = config.SocialActivationDistanceTiles
                                       * config.SocialActivationDistanceTiles;
        var nearby = plan.Candidates
            .Where(candidate => candidate.Status == DailySocialCandidateStatus.Planned
                                && candidate.TimeSlot == activeTimeSlot)
            .Select(candidate => new
            {
                Candidate = candidate,
                Npc = Game1.currentLocation.characters.FirstOrDefault(npc =>
                    npc.Name.Equals(candidate.NpcName, StringComparison.OrdinalIgnoreCase)),
            })
            .Where(item => item.Npc is not null
                           && item.Npc.CanSocialize
                           && !item.Npc.IsInvisible
                           && Vector2.DistanceSquared(Game1.player.Tile, item.Npc.Tile) <= maximumDistanceSquared)
            .Select(item => new
            {
                item.Candidate,
                Npc = item.Npc!,
                Distance = Vector2.DistanceSquared(Game1.player.Tile, item.Npc!.Tile),
            })
            .OrderBy(item => item.Distance)
            .ThenBy(item => item.Candidate.SelectedOrder)
            .FirstOrDefault();
        if (nearby is null)
            return;

        DailySocialCandidate candidate = nearby.Candidate;
        NpcSocialState npcState = player.GetOrCreateNpc(candidate.NpcName);
        if (npcState.CompletedActionIds.Contains(candidate.ActionId))
        {
            candidate.Status = DailySocialCandidateStatus.Completed;
            plan.TriggeredCount = plan.Candidates.Count(value =>
                value.Status == DailySocialCandidateStatus.Completed);
            socialDirty = true;
            return;
        }

        try
        {
            NPC npc = nearby.Npc;
            IReadOnlyList<string> relevantTags = BuildRelevantSocialTags(player, npcState);
            GiftPolicyContext giftContext = CreateGiftPolicyContext(plan, candidate, npcState, relevantTags);
            SocialGiftCandidateSet giftSet = giftPolicyService.BuildCandidateSet(giftContext);
            Monitor.Log(
                $"主动相遇礼物候选 {npc.Name}：候选数={giftSet.Candidates.Count}，阻止原因={giftSet.BlockReason}。",
                LogLevel.Debug);
            NpcGameSnapshot snapshot = BuildNpcGameSnapshot(npc, playerId);
            var request = new AiSocialSceneRequest
            {
                NpcName = npc.Name,
                NpcDisplayName = npc.displayName,
                GameContext = snapshot.SystemPrompt,
                RecentConversation = BuildRecentConversationExcerpt(playerId, npc.Name),
                SignalSummary = BuildSignalSummary(npcState),
                ActivitySummary = activityJournal.BuildPromptSummary(player, Game1.Date.TotalDays),
                GiftCandidates = giftSet.Candidates.Select(gift => new SocialSceneGiftOption(
                    gift.Key,
                    gift.DisplayName,
                    gift.MatchedTags,
                    gift.DisplayHint)).ToArray(),
                FallbackDialogue = "上次聊过以后，我还记着你说的那些。今天正好碰见，就想问问你最近还好吗？",
                Model = config.Model,
                ThinkingType = config.EnableThinking ? "enabled" : "disabled",
                ReasoningEffort = config.ReasoningEffort,
                MaxOutputTokens = Math.Min(config.MaxOutputTokens, 600),
                MaxDialogueCharacters = config.SocialSceneMaxCharacters,
            };

            candidate.Status = DailySocialCandidateStatus.Generating;
            socialDirty = true;
            PersistSocial(force: true);

            screenState.PendingSocialInfo = new PendingSocialSceneInfo(
                playerId,
                candidate.ActionId,
                candidate.TimeSlot,
                npc.Name,
                npc.displayName,
                Game1.currentLocation.NameOrUniqueName,
                Game1.Date.TotalDays,
                giftSet.Candidates,
                relevantTags);
            screenState.PendingSocialScene = aiSocialSceneService.GenerateAsync(
                runtimeApiKey,
                request,
                screenState.SocialCancellation.Token);
            Monitor.Log(
                $"在 {candidate.TimeSlot.ToString().ToLowerInvariant()} 时段遇见 {npc.Name}，正在即时生成主动交谈。",
                LogLevel.Debug);
        }
        catch (Exception ex)
        {
            candidate.Status = IsSocialTimeSlotActive(candidate.TimeSlot)
                ? DailySocialCandidateStatus.Planned
                : DailySocialCandidateStatus.Expired;
            socialDirty = true;
            Monitor.Log($"启动 {candidate.NpcName} 的主动相遇失败：{ex}", LogLevel.Error);
        }
    }

    private void FinishPendingSocialScene(ConversationScreenState screenState)
    {
        Task<AiSocialSceneDecision> task = screenState.PendingSocialScene!;
        PendingSocialSceneInfo info = screenState.PendingSocialInfo!;
        screenState.PendingSocialScene = null;
        screenState.PendingSocialInfo = null;

        if (!TryGetSocialCandidate(
                info.PlayerId,
                info.NpcName,
                info.ActionId,
                out PlayerSocialDirectorState? player,
                out NpcSocialState? npcState,
                out DailySocialPlan? plan,
                out DailySocialCandidate? candidate)
            || candidate!.Status != DailySocialCandidateStatus.Generating)
        {
            return;
        }

        try
        {
            AiSocialSceneDecision decision = task.GetAwaiter().GetResult();
            if (!IsSocialContextCurrent(info))
            {
                candidate.Status = IsSocialTimeSlotActive(candidate.TimeSlot)
                    ? DailySocialCandidateStatus.Planned
                    : DailySocialCandidateStatus.Expired;
                socialDirty = true;
                return;
            }

            SocialGiftCandidate? selectedGift = null;
            if (decision.Action.Equals(SocialSceneActions.Gift, StringComparison.Ordinal))
            {
                GiftPolicyContext currentContext = CreateGiftPolicyContext(
                    plan!,
                    candidate,
                    npcState!,
                    info.RelevantTags);
                SocialGiftSelectionResult selection = npcGiftToolService.ValidateCall(
                    currentContext,
                    NpcGiftToolNames.GiveGift,
                    decision.GiftCandidateId);
                if (selection.Kind == SocialGiftSelectionKind.Gift)
                {
                    selectedGift = selection.Candidate;
                    string boundDialogue = NpcGiftToolService.GuardGiftOfferDialogue(
                        decision.Dialogue,
                        selectedGift!,
                        out bool dialogueReplaced);
                    if (dialogueReplaced)
                    {
                        Monitor.Log(
                            $"{info.NpcName} 的主动送礼台词没有准确写出 {FormatGiftLabel(selectedGift)}，已按真实候选重写。",
                            LogLevel.Warn);
                    }
                    decision = new AiSocialSceneDecision
                    {
                        Dialogue = boundDialogue,
                        Action = decision.Action,
                        GiftCandidateId = decision.GiftCandidateId,
                        MotiveTag = decision.MotiveTag,
                        UsedFallback = decision.UsedFallback,
                        FailureReason = decision.FailureReason,
                    };
                }
                else
                {
                    Monitor.Log(
                        $"{info.NpcName} 的 AI 礼物选择被代码拒绝：{selection.RejectionReason}；改为只交谈。",
                        LogLevel.Debug);
                    decision = ToTalkOnly(decision);
                }
            }

            if (decision.UsedFallback)
            {
                Monitor.Log(
                    $"{info.NpcName} 的主动交谈使用静态后备台词：{decision.FailureReason}",
                    LogLevel.Debug);
            }

            candidate.Status = DailySocialCandidateStatus.Ready;
            screenState.QueuedSocialScene = new QueuedSocialScene(
                info.PlayerId,
                info.ActionId,
                info.TimeSlot,
                info.NpcName,
                info.NpcDisplayName,
                info.LocationName,
                info.TotalDays,
                decision,
                selectedGift,
                info.RelevantTags);
            socialDirty = true;
            PersistSocial(force: false);
        }
        catch (OperationCanceledException)
        {
            candidate.Status = IsSocialTimeSlotActive(candidate.TimeSlot)
                ? DailySocialCandidateStatus.Planned
                : DailySocialCandidateStatus.Expired;
            socialDirty = true;
        }
        catch (Exception ex)
        {
            candidate.Status = IsSocialTimeSlotActive(candidate.TimeSlot)
                ? DailySocialCandidateStatus.Planned
                : DailySocialCandidateStatus.Expired;
            socialDirty = true;
            Monitor.Log($"完成 {info.NpcName} 的主动交谈生成失败：{ex}", LogLevel.Error);
        }
    }

    private void ShowSocialEncounter(ConversationScreenState screenState, QueuedSocialScene scene)
    {
        if (!TryGetSocialCandidate(
                scene.PlayerId,
                scene.NpcName,
                scene.ActionId,
                out _,
                out NpcSocialState? npcState,
                out _,
                out DailySocialCandidate? candidate)
            || candidate!.Status != DailySocialCandidateStatus.Ready
            || !IsSocialContextCurrent(scene))
        {
            ResetSocialCandidateForToday(scene.PlayerId, scene.NpcName, scene.ActionId);
            return;
        }

        NPC? npc = Game1.currentLocation.characters.FirstOrDefault(character =>
            character.Name.Equals(scene.NpcName, StringComparison.OrdinalIgnoreCase));
        float maximumDistanceSquared = config.SocialActivationDistanceTiles
                                       * config.SocialActivationDistanceTiles;
        if (npc is null
            || !npc.CanSocialize
            || npc.IsInvisible
            || Vector2.DistanceSquared(Game1.player.Tile, npc.Tile) > maximumDistanceSquared)
        {
            ResetSocialCandidateForToday(scene.PlayerId, scene.NpcName, scene.ActionId);
            return;
        }

        npc.facePlayer(Game1.player);
        if (scene.Gift is null)
        {
            if (!CompleteSocialEncounter(scene, SocialEncounterOutcome.TalkOnly, gift: null))
                return;

            ShowNpcDialogue(new QueuedDialogue(
                scene.PlayerId,
                scene.NpcName,
                scene.NpcDisplayName,
                SanitizeForDialogue(scene.Decision.Dialogue)));
            return;
        }

        candidate.Status = DailySocialCandidateStatus.Presenting;
        socialDirty = true;
        PersistSocial(force: true);
        screenState.ActiveSocialScene = scene;

        AiProactiveEncounterMenu? menu = null;
        menu = new AiProactiveEncounterMenu(
            scene.NpcName,
            scene.NpcDisplayName,
            SanitizeForDialogue(scene.Decision.Dialogue),
            new[]
            {
                new AiProactiveChoice("accept", $"收下 {FormatGiftLabel(scene.Gift)}", IsDefer: false),
                new AiProactiveChoice("decline", "谢谢，不过这次先不用", IsDefer: true),
            },
            onChoose: choiceId => TryResolveSocialChoice(scene, choiceId),
            onCancel: () => TryResolveSocialChoice(scene, "decline"),
            onClosed: () =>
            {
                if (ReferenceEquals(screenState.SocialMenu, menu))
                {
                    screenState.SocialMenu = null;
                    screenState.ActiveSocialScene = null;
                }
            },
            proactiveUiScale: config.ProactiveUiScale);
        screenState.SocialMenu = menu;
        Game1.activeClickableMenu = menu;
    }

    private bool TryResolveSocialChoice(QueuedSocialScene scene, string choiceId)
    {
        if (!TryGetSocialCandidate(
                scene.PlayerId,
                scene.NpcName,
                scene.ActionId,
                out _,
                out NpcSocialState? npcState,
                out DailySocialPlan? plan,
                out DailySocialCandidate? candidate)
            || candidate!.Status != DailySocialCandidateStatus.Presenting)
        {
            return false;
        }

        if (choiceId.Equals("decline", StringComparison.OrdinalIgnoreCase))
            return CompleteSocialEncounter(scene, SocialEncounterOutcome.GiftDeclined, scene.Gift);
        if (!choiceId.Equals("accept", StringComparison.OrdinalIgnoreCase) || scene.Gift is null)
            return false;

        GiftPolicyContext currentContext = CreateGiftPolicyContext(
            plan!,
            candidate,
            npcState!,
            scene.RelevantTags);
        SocialGiftSelectionResult selection = npcGiftToolService.ValidateCall(
            currentContext,
            NpcGiftToolNames.GiveGift,
            scene.Gift.Key);
        if (selection.Kind != SocialGiftSelectionKind.Gift || selection.Candidate is null)
        {
            Monitor.Log(
                $"交付前再次校验 {scene.NpcName} 的礼物失败：{selection.RejectionReason}。",
                LogLevel.Warn);
            return false;
        }

        if (!TryDeliverNpcGift(
                scene.NpcName,
                selection.Candidate,
                selection.Candidate.Quantity,
                out string deliveryFailure))
        {
            if (deliveryFailure.Length > 0)
                ShowHud(deliveryFailure, HUDMessage.error_type);
            return false;
        }

        bool completed = CompleteSocialEncounter(
            scene,
            SocialEncounterOutcome.GiftAccepted,
            selection.Candidate);
        if (completed)
        {
            ShowHud(
                $"{scene.NpcDisplayName} 送给了你：{FormatGiftLabel(selection.Candidate)}",
                HUDMessage.newQuest_type);
        }
        return completed;
    }

    private bool CompleteSocialEncounter(
        QueuedSocialScene scene,
        SocialEncounterOutcome outcome,
        SocialGiftCandidate? gift)
    {
        if (!TryGetSocialCandidate(
                scene.PlayerId,
                scene.NpcName,
                scene.ActionId,
                out _,
                out NpcSocialState? npcState,
                out DailySocialPlan? plan,
                out DailySocialCandidate? candidate)
            || npcState!.CompletedActionIds.Contains(scene.ActionId))
        {
            return false;
        }

        npcState.CompletedActionIds.Add(scene.ActionId);
        npcState.LastProactiveDay = Game1.Date.TotalDays;
        if (outcome is SocialEncounterOutcome.GiftAccepted or SocialEncounterOutcome.GiftDeclined)
            npcState.LastGiftOfferDay = Game1.Date.TotalDays;
        candidate!.Status = DailySocialCandidateStatus.Completed;
        plan!.TriggeredCount = plan.Candidates.Count(value =>
            value.Status == DailySocialCandidateStatus.Completed);
        if (outcome == SocialEncounterOutcome.GiftAccepted)
        {
            plan.GiftCount++;
            npcState.LastGiftDay = Game1.Date.TotalDays;
            if (gift is not null)
                npcState.RecordGift(gift.QualifiedItemId, Game1.Date.TotalDays);
        }

        RecordSocialEncounterMemory(scene, outcome, gift);
        socialDirty = true;
        PersistSocial(force: true);
        PersistMemory(force: false);
        Monitor.Log(
            $"{scene.NpcName} 的今日主动相遇已完成：{outcome}；不修改原版好感。",
            LogLevel.Info);
        return true;
    }

    private void RecordSocialEncounterMemory(
        QueuedSocialScene scene,
        SocialEncounterOutcome outcome,
        SocialGiftCandidate? gift)
    {
        Dictionary<string, NpcConversationMemory> memories = GetPlayerMemories(scene.PlayerId);
        if (!memories.TryGetValue(scene.NpcName, out NpcConversationMemory? memory) || memory is null)
        {
            memory = new NpcConversationMemory
            {
                PlayerId = scene.PlayerId,
                NpcName = scene.NpcName,
            };
            memories[scene.NpcName] = memory;
        }

        string outcomeText = outcome switch
        {
            SocialEncounterOutcome.GiftAccepted => $"玩家收下了你送的{FormatGiftLabel(gift)}",
            SocialEncounterOutcome.GiftDeclined => $"玩家礼貌地婉拒了你送的{FormatGiftLabel(gift)}",
            _ => "玩家听完了你主动说的话",
        };
        string motive = string.IsNullOrWhiteSpace(scene.Decision.MotiveTag)
            ? string.Empty
            : $"；动机={scene.Decision.MotiveTag}";
        string content = LimitReply(
            $"[主动相遇记录：{outcomeText}{motive}] {scene.Decision.Dialogue}");
        memory.Messages ??= new List<ConversationMemoryMessage>();
        memory.Messages.Add(new ConversationMemoryMessage
        {
            Role = "assistant",
            Content = content,
            GameDate = $"{Game1.Date} {Game1.timeOfDay}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Source = ConversationMemorySources.ModSocial,
        });
        memory.TotalTurns++;
        memory.LastDate = $"{Game1.Date} {Game1.timeOfDay}";
        memoryDirty = true;
    }

    private GiftPolicyContext CreateGiftPolicyContext(
        DailySocialPlan plan,
        DailySocialCandidate candidate,
        NpcSocialState npcState,
        IReadOnlyCollection<string> relevantTags)
        => CreateGiftPolicyContext(
            candidate.ActionId,
            candidate.NpcName,
            npcState,
            relevantTags);

    private GiftPolicyContext CreateGiftPolicyContext(
        string actionId,
        string npcName,
        NpcSocialState npcState,
        IReadOnlyCollection<string>? relevantTags = null,
        string deliveryMode = SocialGiftDeliveryModes.Immediate)
    {
        return new GiftPolicyContext
        {
            ActionId = actionId,
            NpcName = npcName,
            CurrentDay = Game1.Date.TotalDays,
            GiftAlreadyOfferedToday = npcState.LastGiftOfferDay == Game1.Date.TotalDays,
            CompletedActionIds = npcState.CompletedActionIds,
            RelevantTags = relevantTags ?? Array.Empty<string>(),
            HeartLevel = Game1.player.getFriendshipHeartLevelForNPC(npcName),
            DeliveryMode = deliveryMode,
            RecentGifts = npcState.RecentGifts,
        };
    }

    private ConversationGiftExecutionResult ExecuteConversationGiftTool(
        PendingConversationInfo info,
        AiGiftToolDecision decision)
    {
        if (!decision.ShouldUseGiftTool || string.IsNullOrWhiteSpace(info.GiftActionId))
        {
            if (decision.UsedFallback)
            {
                Monitor.Log(
                    $"{info.NpcName} 的礼物规划已安全跳过：{decision.FailureReason}",
                    LogLevel.Debug);
            }
            return ConversationGiftExecutionResult.NoAction(decision.ToolName);
        }

        if (!Context.IsMainPlayer)
        {
            return new ConversationGiftExecutionResult
            {
                RequestedToolName = decision.ToolName,
                Outcome = ConversationGiftOutcome.Rejected,
                FailureReason = "当前玩家不支持持久化礼物。",
            };
        }

        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(info.PlayerId);
        NpcSocialState npcState = player.GetOrCreateNpc(info.NpcName);
        GiftPolicyContext context = CreateGiftPolicyContext(
            info.GiftActionId,
            info.NpcName,
            npcState);
        SocialGiftSelectionResult selection = npcGiftToolService.ValidateCall(
            context,
            decision.ToolName,
            decision.GiftCandidateId);
        if (selection.Kind != SocialGiftSelectionKind.Gift || selection.Candidate is null)
        {
            Monitor.Log(
                $"{info.NpcName} 的 {decision.ToolName} 调用被代码拒绝：{selection.RejectionReason}。",
                LogLevel.Warn);
            return new ConversationGiftExecutionResult
            {
                RequestedToolName = decision.ToolName,
                Outcome = ConversationGiftOutcome.Rejected,
                FailureReason = selection.RejectionReason.ToString(),
            };
        }

        SocialGiftCandidate gift = selection.Candidate;
        int quantity = Math.Clamp(gift.Quantity, 1, 999);
        bool committed;
        string failure;
        ConversationGiftOutcome outcome = ConversationGiftOutcome.ImmediateDelivered;
        if (!decision.ShouldGiveGift)
        {
            committed = false;
            failure = "当面对话不允许安排邮箱礼物。";
        }
        else
        {
            committed = TryDeliverNpcGift(info.NpcName, gift, quantity, out failure);
        }

        if (!committed)
        {
            if (failure.Length > 0)
                ShowHud(failure, HUDMessage.error_type);
            Monitor.Log(
                $"{info.NpcName} 的 {decision.ToolName} 未提交：{failure}",
                LogLevel.Warn);
            return new ConversationGiftExecutionResult
            {
                RequestedToolName = decision.ToolName,
                Outcome = ConversationGiftOutcome.Failed,
                Candidate = gift,
                Quantity = quantity,
                FailureReason = failure,
            };
        }

        npcState.CompletedActionIds.Add(info.GiftActionId);
        npcState.LastGiftOfferDay = Game1.Date.TotalDays;
        npcState.LastGiftDay = Game1.Date.TotalDays;
        npcState.RecordGift(gift.QualifiedItemId, Game1.Date.TotalDays);
        DailySocialPlan? plan = GetCurrentSocialPlan();
        if (plan is not null)
            plan.GiftCount++;
        socialDirty = true;
        PersistSocial(force: true);

        string deliveryText = $"{info.NpcDisplayName} 送给了你：{FormatGiftLabel(gift, quantity)}";
        ShowHud(deliveryText, HUDMessage.newQuest_type);
        Monitor.Log(
            $"{info.NpcName} 调用 {decision.ToolName}({gift.Key})：{outcome}。",
            LogLevel.Info);
        return new ConversationGiftExecutionResult
        {
            RequestedToolName = decision.ToolName,
            Outcome = outcome,
            Candidate = gift,
            Quantity = quantity,
        };
    }

    private static string CreateConversationGiftActionId(int day, string npcName, long conversationTurn)
    {
        string normalizedNpc = SocialModelNormalization.LimitSingleLine(npcName, 80).ToLowerInvariant();
        return SocialModelNormalization.LimitSingleLine(
            $"chat-gift-{day}-{conversationTurn}-{normalizedNpc}",
            128);
    }

    private bool TryDeliverNpcGift(
        string npcName,
        SocialGiftCandidate gift,
        int quantity,
        out string failure)
    {
        failure = string.Empty;
        try
        {
            Item item = ItemRegistry.Create(gift.QualifiedItemId, Math.Clamp(quantity, 1, 999));
            if (Game1.player.couldInventoryAcceptThisItem(item)
                && Game1.player.addItemToInventoryBool(item, makeActiveObject: false))
            {
                return true;
            }

            Game1.createItemDebris(item, Game1.player.Position, -1, Game1.currentLocation);
            ShowHud("背包已满，礼物放在了你脚边。", HUDMessage.error_type);
            return true;
        }
        catch (Exception ex)
        {
            failure = $"{npcName} 的礼物交付失败。";
            Monitor.Log($"无法交付 {npcName} 的礼物：{ex}", LogLevel.Error);
            return false;
        }
    }

    private void PrepareOvernightMailPlan()
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || !config.EnableOvernightMailGifts
            || config.MaxOvernightMailGifts <= 0)
        {
            return;
        }

        string playerId = GetPlayerId();
        int sourceDay = Game1.Date.TotalDays;
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(playerId);
        if (player.PendingOvernightMailPlan?.SourceDay == sourceDay)
            return;
        if (player.PendingOvernightMailPlan is not null)
        {
            Monitor.Log(
                $"丢弃未能在次日完成的隔夜邮件计划 {player.PendingOvernightMailPlan.PlanId}。",
                LogLevel.Warn);
            player.PendingOvernightMailPlan = null;
            socialDirty = true;
        }

        DailyConversationJournalEntry[] todayEntries = (player.ConversationJournal
                ?? new List<DailyConversationJournalEntry>())
            .Where(entry => entry.Day == sourceDay)
            .OrderBy(entry => entry.ConversationTurn)
            .ToArray();
        if (todayEntries.Length == 0)
        {
            Monitor.Log("今天没有完成普通 AI 对话，不创建隔夜邮件计划。", LogLevel.Debug);
            return;
        }

        string activitySummary = activityJournal.BuildPromptSummary(player, sourceDay);
        var npcSnapshots = new List<OvernightMailNpcSnapshot>();
        foreach (IGrouping<string, DailyConversationJournalEntry> group in todayEntries
                     .GroupBy(entry => entry.NpcName, StringComparer.OrdinalIgnoreCase))
        {
            string npcName = group.Key;
            NpcSocialState npcState = player.GetOrCreateNpc(npcName);
            if (npcState.LastGiftOfferDay == sourceDay || npcState.LastGiftDay == sourceDay)
            {
                Monitor.Log(
                    $"隔夜邮件跳过 {npcName}：今天已成功送礼或玩家已明确拒绝礼物。",
                    LogLevel.Debug);
                continue;
            }

            ConversationSignal[] todaySignals = npcState.RecentSignals
                .Where(signal => signal.Day == sourceDay
                                 && group.Any(entry => entry.ConversationTurn == signal.ConversationTurn))
                .ToArray();
            if (!OvernightMailPlannerService.IsEligibleConversation(
                    todaySignals,
                    config.PositiveConversationThreshold))
            {
                Monitor.Log(
                    $"隔夜邮件跳过 {npcName}：当天对话信号不积极、温暖或关切。",
                    LogLevel.Debug);
                continue;
            }

            string actionId = CreateOvernightMailActionId(sourceDay, npcName);
            IReadOnlyList<string> relevantTags = BuildRelevantSocialTags(player, npcState);
            GiftPolicyContext giftContext = CreateGiftPolicyContext(
                actionId,
                npcName,
                npcState,
                relevantTags,
                SocialGiftDeliveryModes.Mail);
            SocialGiftCandidateSet giftSet = giftPolicyService.BuildCandidateSet(giftContext);
            Monitor.Log(
                $"隔夜邮件候选 {npcName}：候选数={giftSet.Candidates.Count}，阻止原因={giftSet.BlockReason}。",
                LogLevel.Debug);
            if (!giftSet.CanOfferGift)
                continue;

            NPC? npc = Game1.getCharacterFromName(
                npcName,
                mustBeVillager: false,
                includeEventActors: false);
            if (npc is null)
            {
                Monitor.Log($"隔夜邮件跳过 {npcName}：无法读取 NPC 存档上下文。", LogLevel.Debug);
                continue;
            }

            string transcript = string.Join(
                "\n",
                group.Select(entry =>
                    $"玩家：{LimitSocialPromptText(entry.PlayerExcerpt, 500)}\n"
                    + $"{entry.NpcDisplayName}：{LimitSocialPromptText(entry.NpcExcerpt, 700)}"));
            var snapshot = new OvernightMailNpcSnapshot
            {
                ActionId = actionId,
                NpcName = npcName,
                NpcDisplayName = group.Last().NpcDisplayName,
                GameContext = BuildNpcGameSnapshot(npc, GetPlayerId()).SystemPrompt,
                ConversationExcerpt = transcript,
                SignalSummary = BuildOvernightSignalSummary(todaySignals),
                ActivitySummary = activitySummary,
                GiftCandidates = giftSet.Candidates.Select(gift => new OvernightMailGiftOption
                {
                    CandidateId = gift.Key,
                    DisplayName = gift.DisplayName,
                    ReasonTags = gift.MatchedTags.ToList(),
                    Hint = gift.DisplayHint,
                }).ToList(),
            };
            snapshot.Normalize();
            npcSnapshots.Add(snapshot);
        }

        if (npcSnapshots.Count == 0)
        {
            Monitor.Log("今天的对话没有产生可安全规划的隔夜邮件候选。", LogLevel.Debug);
            return;
        }

        player.PendingOvernightMailPlan = new OvernightMailPlanSnapshot
        {
            PlanId = $"overnight-mail-plan-{sourceDay}",
            SourceDay = sourceDay,
            DeliverOnOrAfterDay = sourceDay + 1,
            Npcs = npcSnapshots,
        };
        player.PendingOvernightMailPlan.Normalize();
        socialDirty = true;
        PersistSocial(force: true);
        Monitor.Log(
            $"已保存第 {sourceDay} 天的隔夜邮件快照：{npcSnapshots.Count} 名 NPC，最多选择 {config.MaxOvernightMailGifts} 封。",
            LogLevel.Info);
    }

    private void TryStartOvernightMailPlan()
    {
        if (!Context.IsWorldReady
            || !Context.IsMainPlayer
            || !config.EnableOvernightMailGifts
            || config.MaxOvernightMailGifts <= 0
            || pendingOvernightMailTask is not null
            || string.IsNullOrWhiteSpace(runtimeApiKey))
        {
            return;
        }

        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        OvernightMailPlanSnapshot? plan = player.PendingOvernightMailPlan;
        if (plan is null
            || plan.Npcs.Count == 0
            || overnightMailAttemptedPlanId.Equals(plan.PlanId, StringComparison.Ordinal))
        {
            return;
        }

        plan.AttemptCount++;
        overnightMailAttemptedPlanId = plan.PlanId;
        pendingOvernightMailTaskPlanId = plan.PlanId;
        overnightMailDeliveryRetryTicks = 0;
        overnightMailDeliveryRetryCount = 0;
        socialDirty = true;
        PersistSocial(force: true);
        pendingOvernightMailTask = overnightMailPlannerService.PlanAsync(
            runtimeApiKey,
            new OvernightMailPlanRequest
            {
                SourceDay = plan.SourceDay,
                MaximumGiftCount = config.MaxOvernightMailGifts,
                Npcs = plan.Npcs.ToArray(),
                Model = config.Model,
                ThinkingType = config.EnableThinking ? "enabled" : "disabled",
                ReasoningEffort = config.ReasoningEffort,
                MaxOutputTokens = Math.Min(config.MaxOutputTokens, 900),
            },
            overnightMailCancellation.Token);
        Monitor.Log(
            $"开始第 {plan.SourceDay} 天的隔夜邮件 AI 规划（第 {plan.AttemptCount} 次尝试）。",
            LogLevel.Info);
    }

    private void FinishCompletedOvernightMailPlan()
    {
        if (pendingOvernightMailTask is null || !pendingOvernightMailTask.IsCompleted)
            return;
        if (overnightMailDeliveryReadyDay != Game1.Date.TotalDays || !Context.IsPlayerFree)
            return;
        if (overnightMailDeliveryRetryTicks > 0)
        {
            overnightMailDeliveryRetryTicks--;
            return;
        }

        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        OvernightMailPlanSnapshot? plan = player.PendingOvernightMailPlan;
        if (plan is null
            || !plan.PlanId.Equals(pendingOvernightMailTaskPlanId, StringComparison.Ordinal))
        {
            ClearPendingOvernightMailTask();
            return;
        }
        if (Game1.Date.TotalDays < plan.DeliverOnOrAfterDay)
            return;

        Task<OvernightMailPlanDecision> task = pendingOvernightMailTask;
        OvernightMailPlanDecision decision;
        try
        {
            decision = task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            ClearPendingOvernightMailTask();
            return;
        }
        catch (Exception ex)
        {
            decision = new OvernightMailPlanDecision
            {
                UsedFallback = true,
                FailureReason = ex.Message,
            };
        }

        if (decision.UsedFallback)
        {
            Monitor.Log(
                $"隔夜邮件 AI 规划失败：{decision.FailureReason}",
                LogLevel.Warn);
            if (plan.AttemptCount < 2)
            {
                ClearPendingOvernightMailTask();
                overnightMailAttemptedPlanId = string.Empty;
                return;
            }

            ClearPendingOvernightMailTask();
            CompleteOvernightMailPlan(player, plan, deliveredCount: 0);
            return;
        }

        bool retryDelivery = false;
        foreach (OvernightMailGiftDecision giftDecision in decision.Gifts
                     .Take(config.MaxOvernightMailGifts))
        {
            OvernightMailNpcSnapshot? snapshot = plan.Npcs.FirstOrDefault(value =>
                value.NpcName.Equals(giftDecision.NpcName, StringComparison.OrdinalIgnoreCase));
            if (snapshot is null
                || !snapshot.GiftCandidates.Any(candidate => candidate.CandidateId.Equals(
                    giftDecision.GiftCandidateId,
                    StringComparison.Ordinal)))
                continue;

            NpcSocialState npcState = player.GetOrCreateNpc(snapshot.NpcName);
            if (npcState.CompletedActionIds.Contains(snapshot.ActionId))
                continue;
            if (npcState.LastGiftOfferDay == Game1.Date.TotalDays)
            {
                Monitor.Log(
                    $"隔夜邮件取消 {snapshot.NpcName}：今天已经有另一份礼物行动。",
                    LogLevel.Debug);
                continue;
            }

            IReadOnlyList<string> validationTags = snapshot.GiftCandidates
                .SelectMany(candidate => candidate.ReasonTags)
                .Append("general")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            GiftPolicyContext context = CreateGiftPolicyContext(
                snapshot.ActionId,
                snapshot.NpcName,
                npcState,
                validationTags,
                SocialGiftDeliveryModes.Mail);
            SocialGiftSelectionResult selection = npcGiftToolService.ValidateCall(
                context,
                NpcGiftToolNames.MailGift,
                giftDecision.GiftCandidateId);
            if (selection.Kind != SocialGiftSelectionKind.Gift || selection.Candidate is null)
            {
                Monitor.Log(
                    $"隔夜邮件拒绝 {snapshot.NpcName}/{giftDecision.GiftCandidateId}：{selection.RejectionReason}。",
                    LogLevel.Warn);
                continue;
            }

            SocialGiftCandidate gift = selection.Candidate;
            string personalBody = SanitizeMailText(
                giftDecision.LetterBody,
                "昨天聊完以后，我一直记着你说的话。",
                700);
            string letterBody = $"{personalBody} 我在信里放了{FormatGiftLabel(gift)}，希望它能派上用场。";
            if (!TryQueueOvernightMailGift(
                    player,
                    snapshot,
                    gift,
                    letterBody,
                    giftDecision.ReasonTag,
                    out string failure))
            {
                retryDelivery = true;
                if (overnightMailDeliveryRetryCount == 0)
                {
                    Monitor.Log(
                        $"隔夜邮件投递 {snapshot.NpcName} 暂未完成：{failure}；将使用退避策略重试。",
                        LogLevel.Warn);
                }
                continue;
            }

            npcState.CompletedActionIds.Add(snapshot.ActionId);
            npcState.LastGiftOfferDay = Game1.Date.TotalDays;
            npcState.LastGiftDay = Game1.Date.TotalDays;
            npcState.RecordGift(gift.QualifiedItemId, Game1.Date.TotalDays);
            DailySocialPlan? todayPlan = GetCurrentSocialPlan();
            if (todayPlan is not null)
                todayPlan.GiftCount++;
            socialDirty = true;
            Monitor.Log(
                $"隔夜邮件已验证并入箱：NPC={snapshot.NpcName}，候选={gift.Key}，数量={gift.Quantity}，原因={giftDecision.ReasonTag}。",
                LogLevel.Info);
        }

        if (retryDelivery)
        {
            overnightMailDeliveryRetryCount++;
            if (overnightMailDeliveryRetryCount >= 3)
            {
                Monitor.Log(
                    $"隔夜邮件连续 {overnightMailDeliveryRetryCount} 次未能验证正文；已停止本次会话重试，持久化计划保留到下次载入。",
                    LogLevel.Error);
                ClearPendingOvernightMailTask();
                return;
            }

            overnightMailDeliveryRetryTicks = overnightMailDeliveryRetryCount == 1 ? 120 : 300;
            return;
        }

        int deliveredCount = plan.Npcs.Count(snapshot =>
            player.MailGifts.Any(mail => mail.ActionId.Equals(snapshot.ActionId, StringComparison.Ordinal)
                                         && mail.IsQueued
                                         && Game1.player.hasOrWillReceiveMail(mail.MailId)));
        ClearPendingOvernightMailTask();
        CompleteOvernightMailPlan(player, plan, deliveredCount);
    }

    private void ClearPendingOvernightMailTask()
    {
        pendingOvernightMailTask = null;
        pendingOvernightMailTaskPlanId = string.Empty;
        overnightMailDeliveryRetryTicks = 0;
        overnightMailDeliveryRetryCount = 0;
    }

    private void CompleteOvernightMailPlan(
        PlayerSocialDirectorState player,
        OvernightMailPlanSnapshot plan,
        int deliveredCount)
    {
        player.PendingOvernightMailPlan = null;
        player.ConversationJournal.RemoveAll(entry => entry.Day <= plan.SourceDay);
        socialDirty = true;
        PersistSocial(force: true);
        PersistMemory(force: false);
        Monitor.Log(
            $"第 {plan.SourceDay} 天的隔夜邮件计划完成：已验证并入箱 {deliveredCount} 封。",
            LogLevel.Info);
    }

    private bool TryQueueOvernightMailGift(
        PlayerSocialDirectorState player,
        OvernightMailNpcSnapshot snapshot,
        SocialGiftCandidate gift,
        string letterBody,
        string reasonTag,
        out string failure)
    {
        failure = string.Empty;
        string playerId = GetPlayerId();
        string mailId = CreateConversationGiftMailId(playerId, snapshot.ActionId);
        player.MailGifts ??= new List<SocialMailGift>();
        SocialMailGift? existing = player.MailGifts.FirstOrDefault(mail =>
            mail.MailId.Equals(mailId, StringComparison.Ordinal));
        if (existing is not null && Game1.player.hasOrWillReceiveMail(mailId))
            return TryPrimeGiftMailAsset(existing, out failure);
        if (existing is not null)
            player.MailGifts.Remove(existing);

        var mail = new SocialMailGift
        {
            MailId = mailId,
            ActionId = snapshot.ActionId,
            NpcName = snapshot.NpcName,
            NpcDisplayName = snapshot.NpcDisplayName,
            QualifiedItemId = gift.QualifiedItemId,
            GiftDisplayName = gift.DisplayName,
            Quantity = Math.Clamp(gift.Quantity, 1, 999),
            QueuedDay = Game1.Date.TotalDays,
            LetterBody = letterBody,
            ReasonTag = reasonTag,
            IsQueued = true,
            RewardDelivered = false,
        };
        mail.Normalize();
        player.MailGifts.Add(mail);
        socialDirty = true;
        PersistSocial(force: true);
        if (!TryPrimeGiftMailAsset(mail, out failure))
        {
            player.MailGifts.Remove(mail);
            socialDirty = true;
            PersistSocial(force: true);
            InvalidateGiftMailAsset();
            return false;
        }

        try
        {
            if (!Game1.player.mailReceived.Contains(mailId)
                && !Game1.player.mailbox.Contains(mailId))
            {
                Game1.player.mailbox.Insert(0, mailId);
            }
            if (!Game1.player.mailReceived.Contains(mailId)
                && !Game1.player.mailbox.Contains(mailId))
            {
                failure = "当前邮箱没有接受动态邮件。";
                player.MailGifts.Remove(mail);
                socialDirty = true;
                PersistSocial(force: true);
                InvalidateGiftMailAsset();
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            player.MailGifts.Remove(mail);
            socialDirty = true;
            PersistSocial(force: true);
            InvalidateGiftMailAsset();
            failure = $"{snapshot.NpcName} 的当前邮箱投递失败。";
            Monitor.Log($"无法投递 {snapshot.NpcName} 的隔夜邮箱礼物：{ex}", LogLevel.Error);
            return false;
        }
    }

    private bool TryPrimeGiftMailAsset(SocialMailGift mail, out string failure)
    {
        failure = string.Empty;
        try
        {
            InvalidateGiftMailAsset();
            Dictionary<string, string> data = Helper.GameContent.Load<Dictionary<string, string>>(
                GiftMailAssetName);
            foreach (SocialMailGift definition in GetGiftMailDefinitions())
            {
                string expected = GiftMailContentService.Build(definition);
                if (!data.TryGetValue(definition.MailId, out string? actual)
                    || !actual.Equals(expected, StringComparison.Ordinal))
                {
                    failure = $"{definition.NpcName} 的动态邮件正文没有成功注入 {GiftMailAssetName}。";
                    return false;
                }
            }

            Monitor.Log(
                $"动态邮件正文已验证：{mail.MailId}，NPC={mail.NpcName}。",
                LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            failure = $"{mail.NpcName} 的动态邮件正文加载失败。";
            Monitor.Log($"{failure} {ex}", LogLevel.Error);
            return false;
        }
    }

    private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
    {
        TrackVanillaMenuChanged(e);
        if (e.OldMenu is AiChatInputMenu && !ReferenceEquals(e.OldMenu, e.NewMenu))
        {
            ConversationScreenState state = screenStates.Value;
            state.KeyboardSpacePressed = false;
            state.KeyboardSpaceLongTriggered = false;
            state.KeyboardSpaceElapsedTicks = 0;
            CancelVoiceRecording(screenStates.Value);
        }

        if (!Context.IsWorldReady || !Context.IsMainPlayer)
        {
            openGiftMailIds.Value = string.Empty;
            return;
        }

        string watchedMailId = openGiftMailIds.Value;
        if (watchedMailId.Length > 0
            && e.OldMenu is LetterViewerMenu oldLetter
            && !oldLetter.isFromCollection
            && oldLetter.mailTitle.Equals(watchedMailId, StringComparison.Ordinal))
        {
            MarkGiftMailAttachmentClaimed(watchedMailId, "关闭信件时由原版收取");
        }
        openGiftMailIds.Value = string.Empty;

        if (e.NewMenu is not LetterViewerMenu letter
            || letter.isFromCollection
            || string.IsNullOrWhiteSpace(letter.mailTitle)
            || !TryGetGiftMail(letter.mailTitle, out SocialMailGift mail)
            || mail.RewardDelivered)
        {
            return;
        }

        bool hasExpectedAttachment = letter.itemsToGrab.Any(component =>
            component.item is Item item
            && item.QualifiedItemId.Equals(mail.QualifiedItemId, StringComparison.Ordinal)
            && item.Stack == mail.Quantity);
        if (!hasExpectedAttachment)
        {
            RequeueMissingGiftMailAttachment(mail);
            return;
        }

        openGiftMailIds.Value = mail.MailId;
        Monitor.Log(
            $"原版邮件附件已显示：NPC={mail.NpcName}，物品={mail.QualifiedItemId}，数量={mail.Quantity}；等待玩家领取。",
            LogLevel.Debug);
    }

    private void TrackOpenGiftMailAttachment()
    {
        string mailId = openGiftMailIds.Value;
        if (mailId.Length == 0
            || Game1.activeClickableMenu is not LetterViewerMenu letter
            || letter.isFromCollection
            || !letter.mailTitle.Equals(mailId, StringComparison.Ordinal))
        {
            return;
        }

        if (letter.itemsToGrab.All(component => component.item is null))
            MarkGiftMailAttachmentClaimed(mailId, "点击附件领取");
    }

    private bool TryGetGiftMail(string mailId, out SocialMailGift mail)
    {
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        mail = (player.MailGifts ?? new List<SocialMailGift>()).FirstOrDefault(candidate =>
            candidate.MailId.Equals(mailId, StringComparison.Ordinal))!;
        return mail is not null;
    }

    private void MarkGiftMailAttachmentClaimed(string mailId, string claimSource)
    {
        if (!TryGetGiftMail(mailId, out SocialMailGift mail)
            || mail.RewardDelivered)
        {
            openGiftMailIds.Value = string.Empty;
            return;
        }

        mail.RewardDelivered = true;
        mail.RewardDeliveredDay = Game1.Date.TotalDays;
        RecordOvernightMailMemory(mail);
        socialDirty = true;
        PersistSocial(force: true);
        PersistMemory(force: false);
        InvalidateGiftMailAsset();
        RequestGiftMailIntegrityCheck();
        openGiftMailIds.Value = string.Empty;
        ShowHud(
            $"你从 {mail.NpcDisplayName} 的信中领取了：{FormatGiftLabel(new SocialGiftCandidate
            {
                Key = mail.ActionId,
                QualifiedItemId = mail.QualifiedItemId,
                DisplayName = mail.GiftDisplayName,
                Quantity = mail.Quantity,
            })}",
            HUDMessage.newQuest_type);
        Monitor.Log(
            $"邮件附件已由玩家领取：NPC={mail.NpcName}，物品={mail.QualifiedItemId}，数量={mail.Quantity}，方式={claimSource}。",
            LogLevel.Info);
    }

    private void RequeueMissingGiftMailAttachment(SocialMailGift mail)
    {
        while (Game1.player.mailReceived.Remove(mail.MailId))
        {
        }
        while (Game1.player.mailbox.Remove(mail.MailId))
        {
        }
        mail.IsQueued = true;
        socialDirty = true;
        PersistSocial(force: true);
        InvalidateGiftMailAsset();
        RequestGiftMailIntegrityCheck();
        ShowHud($"{mail.NpcDisplayName} 的邮件附件加载失败，验证正文后会重新入箱。", HUDMessage.error_type);
        Monitor.Log(
            $"{mail.NpcName} 的原版邮件附件缺失，已撤销已读状态并移出邮箱，等待正文验证。",
            LogLevel.Error);
    }

    private void RecordOvernightMailMemory(SocialMailGift mail)
    {
        NpcConversationMemory memory = memoryStore.GetOrCreate(GetPlayerId(), mail.NpcName);
        string reason = string.IsNullOrWhiteSpace(mail.ReasonTag) ? string.Empty : $"；原因={mail.ReasonTag}";
        string gift = mail.Quantity > 1
            ? $"{mail.GiftDisplayName} ×{mail.Quantity}"
            : mail.GiftDisplayName;
        memory.Messages.Add(new ConversationMemoryMessage
        {
            Role = "system",
            Content = $"[隔夜邮箱礼物已领取{reason}] 玩家从原版邮件附件中领取了{gift}。",
            GameDate = $"{Game1.Date} {Game1.timeOfDay}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Source = ConversationMemorySources.ModMail,
        });
        memoryDirty = true;
    }

    private static string CreateOvernightMailActionId(int day, string npcName)
    {
        string normalizedNpc = SocialModelNormalization.LimitSingleLine(npcName, 80)
            .ToLowerInvariant();
        return SocialModelNormalization.LimitSingleLine(
            $"overnight-mail-{day}-{normalizedNpc}",
            128);
    }

    private static string BuildOvernightSignalSummary(IEnumerable<ConversationSignal> signals)
    {
        string[] summaries = signals.Select(signal =>
                $"turn={signal.ConversationTurn}, valence={signal.Valence:0.00}, warmth={signal.Warmth:0.00}, "
                + $"concern={signal.Concern:0.00}, confidence={signal.Confidence:0.00}, "
                + $"topics=[{string.Join(",", signal.Topics)}], openLoops=[{string.Join(",", signal.OpenLoops)}]")
            .ToArray();
        return summaries.Length == 0 ? "无" : string.Join("\n", summaries);
    }

    private static string CreateConversationGiftMailId(string playerId, string actionId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{playerId}\u001f{actionId}"));
        return "firstmod.StardewAIMemories.gift." + Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo(GiftMailAssetName))
            return;

        SocialMailGift[] mails = GetGiftMailDefinitions();
        if (mails.Length == 0)
            return;

        e.Edit(asset =>
        {
            IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
            foreach (SocialMailGift mail in mails)
                data[mail.MailId] = GiftMailContentService.Build(mail);
            Monitor.Log(
                $"已向 {GiftMailAssetName} 注入 {mails.Length} 封动态礼物邮件。",
                LogLevel.Debug);
        });
    }

    private SocialMailGift[] GetGiftMailDefinitions()
        => (socialStore.Players ?? new Dictionary<string, PlayerSocialDirectorState>())
            .Values
            .Where(player => player is not null)
            .SelectMany(player => player.MailGifts ?? new List<SocialMailGift>())
            .Where(mail => mail is not null && mail.IsQueued && IsValidGiftMail(mail))
            .GroupBy(mail => mail.MailId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();

    private void InvalidateGiftMailAsset()
    {
        try
        {
            Helper.GameContent.InvalidateCache(asset =>
                asset.NameWithoutLocale.IsEquivalentTo(GiftMailAssetName));
        }
        catch (Exception ex)
        {
            Monitor.Log($"无法刷新动态礼物邮件：{ex}", LogLevel.Warn);
        }
    }

    private static bool IsValidGiftMail(SocialMailGift mail)
        => !string.IsNullOrWhiteSpace(mail.MailId)
           && IsSafeMailIdentifier(mail.MailId)
           && !string.IsNullOrWhiteSpace(mail.NpcDisplayName)
           && !string.IsNullOrWhiteSpace(mail.GiftDisplayName)
           && !string.IsNullOrWhiteSpace(mail.QualifiedItemId)
           && mail.QualifiedItemId.StartsWith(ItemRegistry.type_object, StringComparison.Ordinal)
           && IsSafeMailItemId(mail.QualifiedItemId)
           && ItemRegistry.Exists(mail.QualifiedItemId)
           && mail.Quantity is >= 1 and <= 999;

    private static bool IsSafeMailIdentifier(string value)
        => value.All(character => !char.IsControl(character)
                                 && !char.IsWhiteSpace(character)
                                 && character is not '%' and not '^' and not '[' and not ']');

    private static bool IsSafeMailItemId(string value)
        => value.All(character => !char.IsControl(character)
                                 && !char.IsWhiteSpace(character)
                                 && character is not '%' and not '^' and not '[' and not ']');

    private static string SanitizeMailText(string? value, string fallback, int maximumLength)
    {
        string clean = (value ?? string.Empty)
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("[#]", string.Empty, StringComparison.Ordinal)
            .Replace("^", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        if (clean.Length == 0)
            clean = fallback;
        return clean.Length <= maximumLength ? clean : clean[..maximumLength];
    }

    private static string FormatGiftLabel(SocialGiftCandidate? gift, int? quantity = null)
    {
        if (gift is null)
            return "礼物";

        int stack = Math.Clamp(quantity ?? gift.Quantity, 1, 999);
        return stack > 1 ? $"{gift.DisplayName} ×{stack}" : gift.DisplayName;
    }

    private IReadOnlyList<string> BuildRelevantSocialTags(
        PlayerSocialDirectorState player,
        NpcSocialState npcState)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "general" };
        foreach (ConversationSignal signal in npcState.RecentSignals.TakeLast(3))
        {
            foreach (string topic in signal.Topics)
                AddNormalizedContextTag(tags, topic);
            foreach (string openLoop in signal.OpenLoops)
                AddNormalizedContextTag(tags, openLoop);
            if (signal.Warmth >= 0.65d)
                tags.Add("warm");
        }

        foreach (string rawTag in player.ActivityJournal
                     .Where(day => Game1.Date.TotalDays - day.Day < config.ActivityRetentionDays)
                     .SelectMany(day => day.ActivityTags.Keys))
        {
            AddActivityContextTags(tags, rawTag);
        }

        tags.Add(Game1.Date.SeasonKey.ToLowerInvariant());
        if (Game1.currentLocation.IsRainingHere())
            tags.Add("rain");
        if (Game1.player.Stamina <= Game1.player.MaxStamina * 0.35f)
            tags.Add("low_energy");
        AddActivityContextTags(tags, "visit:" + Game1.currentLocation.NameOrUniqueName.ToLowerInvariant());
        return tags.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Take(48).ToArray();
    }

    private static void AddActivityContextTags(ISet<string> tags, string rawTag)
    {
        string tag = rawTag.Trim().ToLowerInvariant();
        switch (tag)
        {
            case "visit:mines":
                tags.Add("mining");
                tags.Add("cave");
                break;
            case "visit:beach":
                tags.Add("beach");
                tags.Add("fishing");
                break;
            case "visit:forest":
                tags.Add("forest");
                tags.Add("foraging");
                break;
            case "visit:farm":
                tags.Add("farming");
                tags.Add("animals");
                break;
            case "visit:island":
                tags.Add("island");
                break;
            case "visit:desert":
                tags.Add("desert");
                break;
            case "item_gain:mineral":
                tags.Add("mineral");
                tags.Add("mining");
                break;
            case "item_gain:fish":
                tags.Add("fishing");
                break;
            case "item_gain:produce":
            case "item_gain:seed":
                tags.Add("farming");
                break;
            case "item_gain:forage":
                tags.Add("foraging");
                break;
            case "item_gain:monster_loot":
                tags.Add("combat");
                tags.Add("mining");
                break;
            case "item_gain:resource":
                tags.Add("construction");
                tags.Add("crafting");
                break;
            case "active:morning":
                tags.Add("early_morning");
                break;
            case "active:late_night":
            case "stayed_up_late":
                tags.Add("low_energy");
                break;
            case "weather:rain":
                tags.Add("rain");
                break;
            default:
                if (tag.StartsWith("level_up:", StringComparison.Ordinal))
                    tags.Add(tag["level_up:".Length..]);
                break;
        }
    }

    private static void AddNormalizedContextTag(ISet<string> tags, string value)
    {
        string normalized = value.Trim().ToLowerInvariant().Replace(' ', '_');
        if (normalized.Length is > 0 and <= 64
            && normalized.All(character => character is >= 'a' and <= 'z'
                                           or >= '0' and <= '9'
                                           or '_'))
        {
            tags.Add(normalized);
        }
    }

    private string BuildRecentConversationExcerpt(string playerId, string npcName)
    {
        if (!memoryStore.TryGet(playerId, npcName, out NpcConversationMemory? memory)
            || memory is null)
        {
            return "无";
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(memory.Summary))
            builder.Append("长期摘要：").AppendLine(LimitSocialPromptText(memory.Summary, 600));
        foreach (ConversationMemoryMessage message in (memory.Messages ?? new List<ConversationMemoryMessage>()).TakeLast(6))
        {
            string role = (message.Role ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "assistant" => "NPC",
                "user" => "玩家",
                _ => "游戏事实",
            };
            builder.Append(role).Append("：").AppendLine(LimitSocialPromptText(message.Content, 300));
        }

        string result = builder.ToString().Trim();
        return result.Length == 0 ? "无" : LimitSocialPromptText(result, 1600);
    }

    private static string BuildSignalSummary(NpcSocialState npcState)
    {
        string[] summaries = npcState.RecentSignals
            .TakeLast(3)
            .Select(signal =>
                $"day={signal.Day}, valence={signal.Valence:0.00}, warmth={signal.Warmth:0.00}, "
                + $"concern={signal.Concern:0.00}, topics=[{string.Join(",", signal.Topics)}], "
                + $"openLoops=[{string.Join(",", signal.OpenLoops)}]")
            .ToArray();
        return summaries.Length == 0 ? "无" : string.Join("\n", summaries);
    }

    private static string LimitSocialPromptText(string? value, int maximumCharacters)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Trim();
        return clean.Length <= maximumCharacters ? clean : clean[..maximumCharacters] + "…";
    }

    private void RecordCompletedConversationSignal(
        PendingConversationInfo info,
        long conversationTurn,
        string reply)
    {
        if (!Context.IsMainPlayer)
            return;

        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(info.PlayerId);
        NpcSocialState npcState = player.GetOrCreateNpc(info.NpcName);
        player.ConversationJournal ??= new List<DailyConversationJournalEntry>();
        player.ConversationJournal.RemoveAll(entry =>
            entry.Day == info.TotalDays
            && entry.NpcName.Equals(info.NpcName, StringComparison.OrdinalIgnoreCase)
            && entry.ConversationTurn == conversationTurn);
        var journalEntry = new DailyConversationJournalEntry
        {
            Day = info.TotalDays,
            NpcName = info.NpcName,
            NpcDisplayName = info.NpcDisplayName,
            ConversationTurn = conversationTurn,
            PlayerExcerpt = info.UserText,
            NpcExcerpt = reply,
        };
        journalEntry.Normalize();
        player.ConversationJournal.Add(journalEntry);
        player.ConversationJournal = player.ConversationJournal
            .OrderByDescending(entry => entry.Day)
            .ThenByDescending(entry => entry.ConversationTurn)
            .Take(PlayerSocialDirectorState.MaxConversationJournalEntries)
            .OrderBy(entry => entry.Day)
            .ThenBy(entry => entry.ConversationTurn)
            .ToList();
        ConversationSignal neutral = ConversationSignalExtractor.CreateNeutral(
            info.TotalDays,
            conversationTurn);
        npcState.RecentSignals.RemoveAll(signal =>
            signal.Day == neutral.Day && signal.ConversationTurn == neutral.ConversationTurn);
        npcState.RecentSignals.Add(neutral);
        npcState.LastConversationDay = info.TotalDays;
        npcState.Normalize(info.NpcName);
        socialDirty = true;
        PersistSocial(force: false);

        if (!config.EnableConversationSignalAnalysis)
            return;

        Task<ConversationSignalExtractionResult> task = conversationSignalExtractor.ExtractWithDiagnosticsAsync(
            runtimeApiKey,
            info.NpcName,
            info.UserText,
            reply,
            info.TotalDays,
            conversationTurn,
            GetConversationOptions(),
            signalAnalysisCancellation.Token);
        pendingSignalAnalyses.Add(new PendingSignalAnalysis(
            info.PlayerId,
            info.NpcName,
            info.TotalDays,
            conversationTurn,
            task));
    }

    private void RecordCompletedConversationSignalFromGraph(
        PendingConversationInfo info,
        long conversationTurn,
        string reply,
        LangGraphMemoryUpdate update)
    {
        if (!Context.IsMainPlayer)
            return;

        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(info.PlayerId);
        NpcSocialState npcState = player.GetOrCreateNpc(info.NpcName);
        player.ConversationJournal ??= new List<DailyConversationJournalEntry>();
        player.ConversationJournal.RemoveAll(entry =>
            entry.Day == info.TotalDays
            && entry.NpcName.Equals(info.NpcName, StringComparison.OrdinalIgnoreCase)
            && entry.ConversationTurn == conversationTurn);
        var journalEntry = new DailyConversationJournalEntry
        {
            Day = info.TotalDays,
            NpcName = info.NpcName,
            NpcDisplayName = info.NpcDisplayName,
            ConversationTurn = conversationTurn,
            PlayerExcerpt = info.UserText,
            NpcExcerpt = reply,
        };
        journalEntry.Normalize();
        player.ConversationJournal.Add(journalEntry);
        player.ConversationJournal = player.ConversationJournal
            .OrderByDescending(entry => entry.Day)
            .ThenByDescending(entry => entry.ConversationTurn)
            .Take(PlayerSocialDirectorState.MaxConversationJournalEntries)
            .OrderBy(entry => entry.Day)
            .ThenBy(entry => entry.ConversationTurn)
            .ToList();

        var signal = new ConversationSignal
        {
            Day = info.TotalDays,
            ConversationTurn = conversationTurn,
            Valence = config.EnableConversationSignalAnalysis ? update.Signal.Valence : 0d,
            Warmth = config.EnableConversationSignalAnalysis ? update.Signal.Warmth : 0d,
            Concern = config.EnableConversationSignalAnalysis ? update.Signal.Concern : 0d,
            Confidence = config.EnableConversationSignalAnalysis ? update.Signal.Confidence : 0d,
            Topics = config.EnableConversationSignalAnalysis ? update.Topics : new List<string>(),
            OpenLoops = config.EnableConversationSignalAnalysis ? update.OpenLoops : new List<string>(),
        };
        signal.Normalize();
        npcState.RecentSignals.RemoveAll(existing =>
            existing.Day == signal.Day && existing.ConversationTurn == signal.ConversationTurn);
        npcState.RecentSignals.Add(signal);
        npcState.LastConversationDay = info.TotalDays;
        npcState.Normalize(info.NpcName);
        socialDirty = true;
        PersistSocial(force: false);
    }

    private void FinishCompletedSignalAnalyses()
    {
        for (int index = pendingSignalAnalyses.Count - 1; index >= 0; index--)
        {
            PendingSignalAnalysis pending = pendingSignalAnalyses[index];
            if (!pending.Task.IsCompleted)
                continue;

            pendingSignalAnalyses.RemoveAt(index);
            ConversationSignalExtractionResult result;
            try
            {
                result = pending.Task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Monitor.Log($"{pending.NpcName} 的对话信号后台任务失败：{ex.Message}", LogLevel.Debug);
                continue;
            }

            if (!socialStore.TryGetNpc(pending.PlayerId, pending.NpcName, out NpcSocialState? npcState)
                || npcState is null)
            {
                continue;
            }

            int signalIndex = npcState.RecentSignals.FindIndex(signal =>
                signal.Day == pending.Day && signal.ConversationTurn == pending.ConversationTurn);
            if (signalIndex < 0)
                continue;

            npcState.RecentSignals[signalIndex] = result.Signal.CloneNormalized();
            npcState.Normalize(pending.NpcName);
            socialDirty = true;
            if (result.UsedFallback)
            {
                Monitor.Log(
                    $"{pending.NpcName} 的对话信号使用中性后备值：{result.FailureReason}",
                    LogLevel.Debug);
            }
        }
    }

    private bool TryGetSocialCandidate(
        string playerId,
        string npcName,
        string actionId,
        out PlayerSocialDirectorState? player,
        out NpcSocialState? npcState,
        out DailySocialPlan? plan,
        out DailySocialCandidate? candidate)
    {
        player = null;
        npcState = null;
        plan = null;
        candidate = null;
        if (!Context.IsWorldReady)
            return false;
        if (!socialStore.TryGetPlayer(playerId, out player) || player is null)
            return false;
        if (!player.TryGetNpc(npcName, out npcState) || npcState is null)
            return false;
        plan = player.TodayPlan;
        if (plan is null || plan.Day != Game1.Date.TotalDays)
            return false;
        candidate = plan.Candidates.FirstOrDefault(value =>
            value.ActionId.Equals(actionId, StringComparison.Ordinal)
            && value.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase));
        return candidate is not null;
    }

    private void ResetSocialCandidateForToday(string playerId, string npcName, string actionId)
    {
        if (TryGetSocialCandidate(
                playerId,
                npcName,
                actionId,
                out _,
                out NpcSocialState? npcState,
                out _,
                out DailySocialCandidate? candidate)
            && !npcState!.CompletedActionIds.Contains(actionId)
            && candidate!.Status is DailySocialCandidateStatus.Generating
                or DailySocialCandidateStatus.Ready
                or DailySocialCandidateStatus.Presenting)
        {
            candidate.Status = IsSocialTimeSlotActive(candidate.TimeSlot)
                ? DailySocialCandidateStatus.Planned
                : DailySocialCandidateStatus.Expired;
            socialDirty = true;
        }
    }

    private void CancelPendingSocialScene(ConversationScreenState state, bool retryToday)
    {
        string? playerId = state.PendingSocialInfo?.PlayerId
                           ?? state.QueuedSocialScene?.PlayerId
                           ?? state.ActiveSocialScene?.PlayerId;
        string? npcName = state.PendingSocialInfo?.NpcName
                         ?? state.QueuedSocialScene?.NpcName
                         ?? state.ActiveSocialScene?.NpcName;
        string? actionId = state.PendingSocialInfo?.ActionId
                          ?? state.QueuedSocialScene?.ActionId
                          ?? state.ActiveSocialScene?.ActionId;

        if (!state.SocialCancellation.IsCancellationRequested)
            state.SocialCancellation.Cancel();
        state.SocialCancellation.Dispose();
        state.SocialCancellation = new CancellationTokenSource();
        state.PendingSocialScene = null;
        state.PendingSocialInfo = null;
        state.QueuedSocialScene = null;
        state.ActiveSocialScene = null;

        if (state.SocialMenu is not null)
        {
            AiProactiveEncounterMenu menu = state.SocialMenu;
            state.SocialMenu = null;
            menu.Dismiss();
        }

        if (string.IsNullOrWhiteSpace(playerId)
            || string.IsNullOrWhiteSpace(npcName)
            || string.IsNullOrWhiteSpace(actionId)
            || !TryGetSocialCandidate(
                playerId,
                npcName,
                actionId,
                out _,
                out NpcSocialState? npcState,
                out _,
                out DailySocialCandidate? candidate)
            || npcState!.CompletedActionIds.Contains(actionId))
        {
            return;
        }

        candidate!.Status = retryToday && IsSocialTimeSlotActive(candidate.TimeSlot)
            ? DailySocialCandidateStatus.Planned
            : DailySocialCandidateStatus.Expired;
        socialDirty = true;
    }

    private void ExpireTodaySocialPlan()
    {
        DailySocialPlan? plan = GetCurrentSocialPlan();
        if (plan is null)
            return;

        foreach (DailySocialCandidate candidate in plan.Candidates)
        {
            if (candidate.Status is not DailySocialCandidateStatus.Completed
                and not DailySocialCandidateStatus.Cancelled
                and not DailySocialCandidateStatus.Expired)
            {
                candidate.Status = DailySocialCandidateStatus.Expired;
                socialDirty = true;
            }
        }
    }

    private void ExpirePassedSocialWindows()
    {
        DailySocialPlan? plan = GetCurrentSocialPlan();
        if (plan is null)
            return;

        foreach (DailySocialCandidate candidate in plan.Candidates)
        {
            if (!IsSocialTimeSlotPast(candidate.TimeSlot)
                || candidate.Status is DailySocialCandidateStatus.Completed
                    or DailySocialCandidateStatus.Cancelled
                    or DailySocialCandidateStatus.Expired)
            {
                continue;
            }

            candidate.Status = DailySocialCandidateStatus.Expired;
            socialDirty = true;
        }
    }

    private static bool TryGetActiveSocialTimeSlot(out DailySocialTimeSlot timeSlot)
    {
        int time = Game1.timeOfDay;
        if (time >= SocialMorningStartTime && time < SocialEveningStartTime)
        {
            timeSlot = DailySocialTimeSlot.Morning;
            return true;
        }
        if (time >= SocialEveningStartTime && time < SocialEncounterCutoffTime)
        {
            timeSlot = DailySocialTimeSlot.Evening;
            return true;
        }

        timeSlot = default;
        return false;
    }

    private static bool IsSocialTimeSlotActive(DailySocialTimeSlot timeSlot)
        => TryGetActiveSocialTimeSlot(out DailySocialTimeSlot active) && active == timeSlot;

    private static bool IsSocialTimeSlotPast(DailySocialTimeSlot timeSlot)
        => timeSlot switch
        {
            DailySocialTimeSlot.Morning => Game1.timeOfDay >= SocialEveningStartTime,
            DailySocialTimeSlot.Evening => Game1.timeOfDay >= SocialEncounterCutoffTime,
            _ => true,
        };

    private bool RepairSocialStoreAfterLoad()
    {
        bool changed = false;
        socialStore.Normalize();
        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        if (!player.LegacyMigrationCompleted)
        {
            // 0.5 narrative trust, affection, flags, and pending nodes intentionally do not
            // influence 0.6 candidate scoring. The old save key is left untouched for rollback.
            player.LegacyMigrationCompleted = true;
            changed = true;
        }

        if (RepairGiftMailsAfterLoad(player))
            changed = true;

        DailySocialPlan? plan = player.TodayPlan;
        if (plan is null || plan.Day != Game1.Date.TotalDays)
            return changed;

        foreach (DailySocialCandidate candidate in plan.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.ActionId))
            {
                candidate.Status = DailySocialCandidateStatus.Cancelled;
                changed = true;
                continue;
            }

            NpcSocialState npcState = player.GetOrCreateNpc(candidate.NpcName);
            if (npcState.CompletedActionIds.Contains(candidate.ActionId))
            {
                if (candidate.Status != DailySocialCandidateStatus.Completed)
                {
                    candidate.Status = DailySocialCandidateStatus.Completed;
                    changed = true;
                }
            }
            else if (candidate.Status is DailySocialCandidateStatus.Generating
                     or DailySocialCandidateStatus.Ready
                     or DailySocialCandidateStatus.Presenting)
            {
                candidate.Status = DailySocialCandidateStatus.Planned;
                changed = true;
            }
        }

        int completedCount = plan.Candidates.Count(candidate =>
            candidate.Status == DailySocialCandidateStatus.Completed);
        if (plan.TriggeredCount != completedCount)
        {
            plan.TriggeredCount = completedCount;
            changed = true;
        }
        plan.GiftCount = Math.Max(0, plan.GiftCount);
        return changed;
    }

    private bool RepairGiftMailsAfterLoad(PlayerSocialDirectorState player)
    {
        bool changed = false;
        player.MailGifts ??= new List<SocialMailGift>();
        foreach (SocialMailGift mail in player.MailGifts.ToArray())
        {
            if (!IsValidGiftMail(mail))
            {
                player.MailGifts.Remove(mail);
                changed = true;
                continue;
            }

            bool queuedByGame = Game1.player.hasOrWillReceiveMail(mail.MailId);
            if (!mail.IsQueued)
            {
                if (queuedByGame)
                {
                    mail.IsQueued = true;
                    changed = true;
                }
                else
                {
                    player.MailGifts.Remove(mail);
                    changed = true;
                    continue;
                }
            }

            if (!mail.RewardDelivered)
                giftMailIntegrityCheckPending = true;
        }

        return changed;
    }

    private bool CleanupReceivedGiftMails(PlayerSocialDirectorState player)
        => RepairGiftMailsAfterLoad(player);

    private void RequestGiftMailIntegrityCheck()
    {
        giftMailIntegrityCheckPending = true;
        giftMailIntegrityRetryTicks = 0;
        giftMailIntegrityRetryCount = 0;
    }

    private void EnsureGiftMailInboxIntegrity()
    {
        if (!giftMailIntegrityCheckPending
            || !Context.IsWorldReady
            || !Context.IsMainPlayer
            || overnightMailDeliveryReadyDay != Game1.Date.TotalDays
            || !Context.IsPlayerFree)
        {
            return;
        }
        if (giftMailIntegrityRetryTicks > 0)
        {
            giftMailIntegrityRetryTicks--;
            return;
        }

        PlayerSocialDirectorState player = socialStore.GetOrCreatePlayer(GetPlayerId());
        SocialMailGift[] unclaimed = (player.MailGifts ?? new List<SocialMailGift>())
            .Where(mail => mail.IsQueued && !mail.RewardDelivered && IsValidGiftMail(mail))
            .ToArray();
        if (unclaimed.Length == 0)
        {
            giftMailIntegrityCheckPending = false;
            giftMailIntegrityRetryCount = 0;
            return;
        }

        if (!TryPrimeGiftMailAsset(unclaimed[0], out string failure))
        {
            foreach (SocialMailGift mail in unclaimed)
            {
                while (Game1.player.mailbox.Remove(mail.MailId))
                {
                }
            }

            giftMailIntegrityRetryCount++;
            if (giftMailIntegrityRetryCount == 1)
            {
                Monitor.Log(
                    $"动态邮件正文尚未通过完整性检查，已暂时移出原版邮箱：{failure}",
                    LogLevel.Warn);
            }
            if (giftMailIntegrityRetryCount >= 3)
            {
                giftMailIntegrityCheckPending = false;
                Monitor.Log(
                    "动态邮件正文连续 3 次未通过完整性检查；邮件状态已保留，但本次会话不再加入邮箱。",
                    LogLevel.Error);
                return;
            }

            giftMailIntegrityRetryTicks = giftMailIntegrityRetryCount == 1 ? 120 : 300;
            return;
        }

        bool restoredAny = false;
        foreach (SocialMailGift mail in unclaimed)
        {
            if (Game1.player.mailForTomorrow.Contains(mail.MailId))
                continue;

            while (Game1.player.mailReceived.Remove(mail.MailId))
            {
                restoredAny = true;
            }
            if (!Game1.player.mailbox.Contains(mail.MailId))
            {
                Game1.player.mailbox.Insert(0, mail.MailId);
                restoredAny = true;
            }
        }

        giftMailIntegrityCheckPending = false;
        giftMailIntegrityRetryTicks = 0;
        giftMailIntegrityRetryCount = 0;
        if (restoredAny)
        {
            socialDirty = true;
            PersistSocial(force: true);
            Monitor.Log(
                $"动态邮件正文与原版附件已验证，恢复入箱 {unclaimed.Count(mail => !Game1.player.mailForTomorrow.Contains(mail.MailId))} 封。",
                LogLevel.Info);
        }
    }

    private void PersistSocial(bool force)
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer || (!force && !socialDirty))
            return;

        try
        {
            socialStore.Normalize();
            Helper.Data.WriteSaveData(SocialDirectorSaveDataKey, socialStore);
            socialDirty = false;
        }
        catch (Exception ex)
        {
            Monitor.Log($"保存 NPC 社交导演状态失败：{ex}", LogLevel.Error);
        }
    }

    private void ForgetSocialSignals(string playerId, string? npcName)
    {
        if (!socialStore.TryGetPlayer(playerId, out PlayerSocialDirectorState? player) || player is null)
            return;

        foreach (ConversationScreenState state in screenStates.GetActiveValues().Select(pair => pair.Value))
        {
            bool matches = npcName is null
                           || state.PendingSocialInfo?.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase) == true
                           || state.QueuedSocialScene?.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase) == true
                           || state.ActiveSocialScene?.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase) == true;
            if (matches)
                CancelPendingSocialScene(state, retryToday: false);
        }

        IEnumerable<NpcSocialState> states = npcName is null
            ? player.NpcStates.Values
            : player.NpcStates.Values.Where(state =>
                state.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase));
        foreach (NpcSocialState state in states)
        {
            state.RecentSignals.Clear();
            state.RecentGifts.Clear();
            state.LastConversationDay = -1;
        }

        if (player.TodayPlan is not null)
        {
            foreach (DailySocialCandidate candidate in player.TodayPlan.Candidates.Where(candidate =>
                         npcName is null || candidate.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase)))
            {
                if (candidate.Status != DailySocialCandidateStatus.Completed)
                    candidate.Status = DailySocialCandidateStatus.Cancelled;
            }
        }

        pendingSignalAnalyses.RemoveAll(pending =>
            pending.PlayerId.Equals(playerId, StringComparison.Ordinal)
            && (npcName is null || pending.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase)));
        player.ConversationJournal.RemoveAll(entry =>
            npcName is null || entry.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase));
        if (player.PendingOvernightMailPlan is not null)
        {
            if (npcName is null)
            {
                player.PendingOvernightMailPlan = null;
            }
            else
            {
                player.PendingOvernightMailPlan.Npcs.RemoveAll(snapshot =>
                    snapshot.NpcName.Equals(npcName, StringComparison.OrdinalIgnoreCase));
                if (player.PendingOvernightMailPlan.Npcs.Count == 0)
                    player.PendingOvernightMailPlan = null;
            }
        }
        socialDirty = true;
    }

    private void OnSocialStatusCommand(string command, string[] args)
    {
        if (!Context.IsWorldReady)
        {
            Monitor.Log("请先载入存档。", LogLevel.Alert);
            return;
        }

        EnsureTodaySocialPlan(persistImmediately: true);
        DailySocialPlan? plan = GetCurrentSocialPlan();
        if (plan is null)
        {
            Monitor.Log("当前玩家没有可用的今日社交计划。", LogLevel.Info);
            return;
        }

        string? npcFilter = args.Length > 0 ? args[0].Trim() : null;
        DailySocialCandidate[] candidates = plan.Candidates
            .Where(candidate => string.IsNullOrWhiteSpace(npcFilter)
                                || candidate.NpcName.Equals(npcFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.SelectedOrder)
            .ToArray();
        string details = candidates.Length == 0
            ? "无候选"
            : string.Join("；", candidates.Select(candidate =>
                $"{candidate.NpcName}/{candidate.TimeSlot}={candidate.Status}, score={candidate.Score:0.000}, tags={string.Join(",", candidate.ReasonTags)}"));
        Monitor.Log(
            $"第 {plan.Day} 天：已完成 {plan.TriggeredCount}/{plan.Candidates.Count} 次，"
            + $"已收礼 {plan.GiftCount} 份；{details}。",
            LogLevel.Info);
    }

    private void ResetSocialBackgroundWork()
    {
        if (!signalAnalysisCancellation.IsCancellationRequested)
            signalAnalysisCancellation.Cancel();
        signalAnalysisCancellation.Dispose();
        signalAnalysisCancellation = new CancellationTokenSource();
        pendingSignalAnalyses.Clear();

        if (!overnightMailCancellation.IsCancellationRequested)
            overnightMailCancellation.Cancel();
        overnightMailCancellation.Dispose();
        overnightMailCancellation = new CancellationTokenSource();
        pendingOvernightMailTask = null;
        pendingOvernightMailTaskPlanId = string.Empty;
        overnightMailAttemptedPlanId = string.Empty;
        overnightMailDeliveryRetryTicks = 0;
        overnightMailDeliveryRetryCount = 0;
        overnightMailDeliveryReadyDay = -1;
        giftMailIntegrityCheckPending = false;
        giftMailIntegrityRetryTicks = 0;
        giftMailIntegrityRetryCount = 0;
        openGiftMailIds.ResetAllScreens();
    }

    private static bool IsSocialContextCurrent(PendingSocialSceneInfo info)
        => Context.IsWorldReady
           && GetPlayerId().Equals(info.PlayerId, StringComparison.Ordinal)
           && Game1.Date.TotalDays == info.TotalDays
           && IsSocialTimeSlotActive(info.TimeSlot)
           && Game1.currentLocation.NameOrUniqueName.Equals(info.LocationName, StringComparison.Ordinal);

    private static bool IsSocialContextCurrent(QueuedSocialScene scene)
        => Context.IsWorldReady
           && GetPlayerId().Equals(scene.PlayerId, StringComparison.Ordinal)
           && Game1.Date.TotalDays == scene.TotalDays
           && IsSocialTimeSlotActive(scene.TimeSlot)
           && Game1.currentLocation.NameOrUniqueName.Equals(scene.LocationName, StringComparison.Ordinal);

    private static AiSocialSceneDecision ToTalkOnly(AiSocialSceneDecision decision)
        => new()
        {
            Dialogue = decision.Dialogue,
            Action = SocialSceneActions.TalkOnly,
            GiftCandidateId = null,
            MotiveTag = decision.MotiveTag,
            UsedFallback = decision.UsedFallback,
            FailureReason = decision.FailureReason,
        };

    private sealed record PendingSignalAnalysis(
        string PlayerId,
        string NpcName,
        int Day,
        long ConversationTurn,
        Task<ConversationSignalExtractionResult> Task);

    private sealed record PendingSocialSceneInfo(
        string PlayerId,
        string ActionId,
        DailySocialTimeSlot TimeSlot,
        string NpcName,
        string NpcDisplayName,
        string LocationName,
        int TotalDays,
        IReadOnlyList<SocialGiftCandidate> GiftCandidates,
        IReadOnlyList<string> RelevantTags);

    private sealed record QueuedSocialScene(
        string PlayerId,
        string ActionId,
        DailySocialTimeSlot TimeSlot,
        string NpcName,
        string NpcDisplayName,
        string LocationName,
        int TotalDays,
        AiSocialSceneDecision Decision,
        SocialGiftCandidate? Gift,
        IReadOnlyList<string> RelevantTags);

    private enum SocialEncounterOutcome
    {
        TalkOnly,
        GiftAccepted,
        GiftDeclined,
    }
}
