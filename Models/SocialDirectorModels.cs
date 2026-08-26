using System;
using System.Collections.Generic;
using System.Linq;

namespace VivantValley;

/// <summary>Persisted state for daily, memory-driven NPC encounters.</summary>
public sealed class SocialDirectorSaveStore
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public Dictionary<string, PlayerSocialDirectorState> Players { get; set; }
        = new(StringComparer.Ordinal);

    public PlayerSocialDirectorState GetOrCreatePlayer(string playerId)
    {
        playerId = SocialModelNormalization.RequireIdentifier(playerId, nameof(playerId));
        Players ??= new Dictionary<string, PlayerSocialDirectorState>(StringComparer.Ordinal);

        if (!Players.TryGetValue(playerId, out PlayerSocialDirectorState? state) || state is null)
        {
            state = new PlayerSocialDirectorState { PlayerId = playerId };
            Players[playerId] = state;
        }

        state.Normalize(playerId);
        return state;
    }

    public NpcSocialState GetOrCreateNpc(string playerId, string npcName)
        => GetOrCreatePlayer(playerId).GetOrCreateNpc(npcName);

    public bool TryGetPlayer(string playerId, out PlayerSocialDirectorState? state)
    {
        state = null;
        return !string.IsNullOrWhiteSpace(playerId)
               && Players is not null
               && Players.TryGetValue(playerId.Trim(), out state)
               && state is not null;
    }

    public bool TryGetNpc(string playerId, string npcName, out NpcSocialState? state)
    {
        state = null;
        return TryGetPlayer(playerId, out PlayerSocialDirectorState? player)
               && player!.TryGetNpc(npcName, out state);
    }

    /// <summary>Repairs null, duplicate, and out-of-range data after deserialization.</summary>
    public void Normalize()
    {
        SchemaVersion = Math.Max(CurrentSchemaVersion, SchemaVersion);
        var normalizedPlayers = new Dictionary<string, PlayerSocialDirectorState>(StringComparer.Ordinal);

        foreach ((string rawPlayerId, PlayerSocialDirectorState? rawState) in Players
                     ?? new Dictionary<string, PlayerSocialDirectorState>())
        {
            string playerId = SocialModelNormalization.NormalizeIdentifier(rawPlayerId);
            if (playerId.Length == 0 || rawState is null)
                continue;

            rawState.Normalize(playerId);
            if (normalizedPlayers.TryGetValue(playerId, out PlayerSocialDirectorState? existing))
                existing.MergeFrom(rawState);
            else
                normalizedPlayers[playerId] = rawState;
        }

        Players = normalizedPlayers;
    }

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (SchemaVersion < CurrentSchemaVersion)
            issues.Add($"schemaVersion must be at least {CurrentSchemaVersion}.");
        if (Players is null)
        {
            issues.Add("players cannot be null.");
            return issues;
        }

        foreach ((string playerId, PlayerSocialDirectorState? player) in Players)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                issues.Add("players contains an empty player ID.");
            if (player is null)
            {
                issues.Add($"player '{playerId}' has a null state.");
                continue;
            }

            issues.AddRange(player.Validate().Select(issue => $"player '{playerId}': {issue}"));
        }

        return issues;
    }
}

/// <summary>All Social Director state owned by one player in a save.</summary>
public sealed class PlayerSocialDirectorState
{
    public const int MaxActivityDays = 7;
    public const int MaxConversationJournalEntries = 32;

    public string PlayerId { get; set; } = string.Empty;

    /// <summary>The persisted plan for the current day. It prevents save reloads from rerolling.</summary>
    public DailySocialPlan? TodayPlan { get; set; }

    public Dictionary<string, NpcSocialState> NpcStates { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public List<DailyActivitySummary> ActivityJournal { get; set; } = new();

    /// <summary>Dynamic mail definitions retained until the game confirms the letter was received.</summary>
    public List<SocialMailGift> MailGifts { get; set; } = new();

    /// <summary>Bounded same-day transcripts used to prepare one overnight surprise-mail request.</summary>
    public List<DailyConversationJournalEntry> ConversationJournal { get; set; } = new();

    /// <summary>A durable API work item which can resume after saving, loading, or returning to title.</summary>
    public OvernightMailPlanSnapshot? PendingOvernightMailPlan { get; set; }

    public bool LegacyMigrationCompleted { get; set; }

    public NpcSocialState GetOrCreateNpc(string npcName)
    {
        npcName = SocialModelNormalization.RequireIdentifier(npcName, nameof(npcName));
        NpcStates ??= new Dictionary<string, NpcSocialState>(StringComparer.OrdinalIgnoreCase);

        if (!NpcStates.TryGetValue(npcName, out NpcSocialState? state) || state is null)
        {
            state = new NpcSocialState { NpcName = npcName };
            NpcStates[npcName] = state;
        }

        state.Normalize(npcName);
        return state;
    }

    public bool TryGetNpc(string npcName, out NpcSocialState? state)
    {
        state = null;
        return !string.IsNullOrWhiteSpace(npcName)
               && NpcStates is not null
               && NpcStates.TryGetValue(npcName.Trim(), out state)
               && state is not null;
    }

    public void Normalize(string playerId)
    {
        PlayerId = SocialModelNormalization.RequireIdentifier(playerId, nameof(playerId));
        TodayPlan?.Normalize();

        var normalizedStates = new Dictionary<string, NpcSocialState>(StringComparer.OrdinalIgnoreCase);
        foreach ((string rawNpcName, NpcSocialState? rawState) in NpcStates
                     ?? new Dictionary<string, NpcSocialState>())
        {
            string npcName = SocialModelNormalization.NormalizeIdentifier(rawNpcName);
            if (npcName.Length == 0 || rawState is null)
                continue;

            rawState.Normalize(npcName);
            if (normalizedStates.TryGetValue(npcName, out NpcSocialState? existing))
                existing.MergeFrom(rawState);
            else
                normalizedStates[npcName] = rawState;
        }

        NpcStates = normalizedStates;
        ActivityJournal = (ActivityJournal ?? new List<DailyActivitySummary>())
            .Where(summary => summary is not null)
            .Select(summary =>
            {
                summary.Normalize();
                return summary;
            })
            .Where(summary => summary.Day >= 0)
            .GroupBy(summary => summary.Day)
            .Select(group => DailyActivitySummary.Merge(group))
            .OrderByDescending(summary => summary.Day)
            .Take(MaxActivityDays)
            .OrderBy(summary => summary.Day)
            .ToList();

        MailGifts = (MailGifts ?? new List<SocialMailGift>())
            .Where(mail => mail is not null)
            .Select(mail =>
            {
                mail.Normalize();
                return mail;
            })
            .Where(mail => mail.MailId.Length > 0
                           && mail.ActionId.Length > 0
                           && mail.QualifiedItemId.Length > 0)
            .GroupBy(mail => mail.MailId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(mail => mail.QueuedDay).First())
            .OrderBy(mail => mail.QueuedDay)
            .ThenBy(mail => mail.MailId, StringComparer.Ordinal)
            .ToList();

        ConversationJournal = (ConversationJournal ?? new List<DailyConversationJournalEntry>())
            .Where(entry => entry is not null)
            .Select(entry =>
            {
                entry.Normalize();
                return entry;
            })
            .Where(entry => entry.Day >= 0
                            && entry.NpcName.Length > 0
                            && entry.ConversationTurn > 0)
            .GroupBy(entry => $"{entry.Day}\u001f{entry.NpcName}\u001f{entry.ConversationTurn}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderByDescending(entry => entry.Day)
            .ThenByDescending(entry => entry.ConversationTurn)
            .Take(MaxConversationJournalEntries)
            .OrderBy(entry => entry.Day)
            .ThenBy(entry => entry.ConversationTurn)
            .ToList();

        PendingOvernightMailPlan?.Normalize();
        if (PendingOvernightMailPlan is not null
            && (PendingOvernightMailPlan.PlanId.Length == 0
                || PendingOvernightMailPlan.SourceDay < 0
                || PendingOvernightMailPlan.Npcs.Count == 0))
        {
            PendingOvernightMailPlan = null;
        }
    }

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(PlayerId))
            issues.Add("playerId is required.");
        if (NpcStates is null)
            issues.Add("npcStates cannot be null.");
        if (ActivityJournal is null)
            issues.Add("activityJournal cannot be null.");
        else if (ActivityJournal.Count > MaxActivityDays)
            issues.Add($"activityJournal cannot contain more than {MaxActivityDays} days.");
        if (MailGifts is null)
            issues.Add("mailGifts cannot be null.");
        else if (MailGifts.Select(mail => mail?.MailId)
                     .Where(mailId => !string.IsNullOrWhiteSpace(mailId))
                     .Distinct(StringComparer.Ordinal)
                     .Count()
                 != MailGifts.Count(mail => !string.IsNullOrWhiteSpace(mail?.MailId)))
        {
            issues.Add("mailGifts cannot contain duplicate mail IDs.");
        }
        if (ConversationJournal is null)
            issues.Add("conversationJournal cannot be null.");
        else if (ConversationJournal.Count > MaxConversationJournalEntries)
            issues.Add($"conversationJournal cannot contain more than {MaxConversationJournalEntries} entries.");

        if (TodayPlan is not null)
            issues.AddRange(TodayPlan.Validate().Select(issue => $"todayPlan: {issue}"));
        return issues;
    }

    internal void MergeFrom(PlayerSocialDirectorState other)
    {
        if (other.TodayPlan is not null
            && (TodayPlan is null || other.TodayPlan.Day > TodayPlan.Day))
        {
            TodayPlan = other.TodayPlan;
        }

        foreach ((string npcName, NpcSocialState state) in other.NpcStates)
        {
            if (NpcStates.TryGetValue(npcName, out NpcSocialState? existing))
                existing.MergeFrom(state);
            else
                NpcStates[npcName] = state;
        }

        ActivityJournal.AddRange(other.ActivityJournal);
        MailGifts.AddRange(other.MailGifts ?? new List<SocialMailGift>());
        ConversationJournal.AddRange(other.ConversationJournal ?? new List<DailyConversationJournalEntry>());
        if (other.PendingOvernightMailPlan is not null
            && (PendingOvernightMailPlan is null
                || other.PendingOvernightMailPlan.SourceDay > PendingOvernightMailPlan.SourceDay))
        {
            PendingOvernightMailPlan = other.PendingOvernightMailPlan;
        }
        LegacyMigrationCompleted |= other.LegacyMigrationCompleted;
        Normalize(PlayerId);
    }
}

/// <summary>A persisted dynamic letter and its vanilla attachment claim state.</summary>
public sealed class SocialMailGift
{
    public string MailId { get; set; } = string.Empty;

    public string ActionId { get; set; } = string.Empty;

    public string NpcName { get; set; } = string.Empty;

    public string NpcDisplayName { get; set; } = string.Empty;

    public string QualifiedItemId { get; set; } = string.Empty;

    public string GiftDisplayName { get; set; } = string.Empty;

    public int Quantity { get; set; } = 1;

    public int QueuedDay { get; set; } = -1;

    /// <summary>False only during the persisted prepare step before the game mail queue is updated.</summary>
    public bool IsQueued { get; set; }

    /// <summary>Plain, control-code-free prose used before the code-owned vanilla attachment command.</summary>
    public string LetterBody { get; set; } = string.Empty;

    public string ReasonTag { get; set; } = string.Empty;

    /// <summary>True only after the vanilla letter menu handed its attachment to the player.</summary>
    public bool RewardDelivered { get; set; }

    public int RewardDeliveredDay { get; set; } = -1;

    /// <summary>Legacy 0.9.1 direct-delivery counter, retained for save compatibility.</summary>
    public int RewardDeliveryAttempts { get; set; }

    public void Normalize()
    {
        MailId = SocialModelNormalization.LimitSingleLine(MailId, 160);
        ActionId = SocialModelNormalization.LimitSingleLine(ActionId, 128);
        NpcName = SocialModelNormalization.LimitSingleLine(NpcName, 80);
        NpcDisplayName = SocialModelNormalization.LimitSingleLine(NpcDisplayName, 80);
        QualifiedItemId = SocialModelNormalization.LimitSingleLine(QualifiedItemId, 80);
        GiftDisplayName = SocialModelNormalization.LimitSingleLine(GiftDisplayName, 120);
        Quantity = Math.Clamp(Quantity, 1, 999);
        QueuedDay = Math.Max(-1, QueuedDay);
        LetterBody = SocialModelNormalization.LimitSingleLine(LetterBody, 1200);
        ReasonTag = SocialModelNormalization.LimitSingleLine(ReasonTag, 64).ToLowerInvariant();
        if (ReasonTag.Any(character => character is not (>= 'a' and <= 'z')
                                                   and not (>= '0' and <= '9')
                                                   and not '_'))
        {
            ReasonTag = string.Empty;
        }
        RewardDeliveredDay = Math.Max(-1, RewardDeliveredDay);
        RewardDeliveryAttempts = Math.Clamp(RewardDeliveryAttempts, 0, 3);
        if (!RewardDelivered)
            RewardDeliveredDay = -1;
    }
}

public enum DailySocialCandidateStatus
{
    Planned,
    Generating,
    Ready,
    Presenting,
    Completed,
    Expired,
    Cancelled,
}

public enum DailySocialTimeSlot
{
    Morning,
    Afternoon,
}

/// <summary>A deterministic set of NPC opportunities persisted for one game day.</summary>
public sealed class DailySocialPlan
{
    public int Day { get; set; } = -1;

    public int PlannerVersion { get; set; } = 1;

    /// <summary>Lower-case hexadecimal SHA-256 digest used to initialize the planner RNG.</summary>
    public string Seed { get; set; } = string.Empty;

    public bool ControllerMode { get; set; }

    public int TriggeredCount { get; set; }

    public int GiftCount { get; set; }

    public List<DailySocialCandidate> Candidates { get; set; } = new();

    public void Normalize()
    {
        Day = Math.Max(-1, Day);
        PlannerVersion = Math.Max(1, PlannerVersion);
        Seed = SocialModelNormalization.LimitSingleLine(Seed, 128).ToLowerInvariant();

        var seenOpportunities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Candidates = (Candidates ?? new List<DailySocialCandidate>())
            .Where(candidate => candidate is not null)
            .Select(candidate =>
            {
                candidate.Normalize();
                return candidate;
            })
            .Where(candidate => candidate.NpcName.Length > 0
                                && seenOpportunities.Add($"{candidate.NpcName}\u001f{candidate.TimeSlot}"))
            .Take(DailySocialPlannerOptions.AbsoluteMaximumCandidates * 2)
            .ToList();

        TriggeredCount = Math.Clamp(TriggeredCount, 0, Candidates.Count);
        GiftCount = Math.Max(0, GiftCount);
    }

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (Day < 0)
            issues.Add("day must be non-negative.");
        if (PlannerVersion < 1)
            issues.Add("plannerVersion must be at least 1.");
        if (Seed.Length != 64 || Seed.Any(character => !Uri.IsHexDigit(character)))
            issues.Add("seed must be a 64-character SHA-256 hex digest.");
        if (Candidates is null)
            issues.Add("candidates cannot be null.");
        else if (Candidates
                     .Where(candidate => candidate is not null)
                     .Select(candidate => $"{candidate!.NpcName}\u001f{candidate.TimeSlot}")
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Count()
                 != Candidates.Count)
            issues.Add("candidates cannot contain duplicate NPC/time-slot opportunities.");
        if (Candidates is not null
            && Candidates.Any(candidate => string.IsNullOrWhiteSpace(candidate?.ActionId)))
        {
            issues.Add("every candidate must have a non-empty actionId.");
        }
        if (Candidates is not null
            && Candidates.Select(candidate => candidate?.ActionId)
                .Where(actionId => !string.IsNullOrWhiteSpace(actionId))
                .Distinct(StringComparer.Ordinal)
                .Count()
               != Candidates.Count(candidate => !string.IsNullOrWhiteSpace(candidate?.ActionId)))
        {
            issues.Add("candidate actionIds must be unique.");
        }
        return issues;
    }
}

public sealed class DailySocialCandidate
{
    public string NpcName { get; set; } = string.Empty;

    public string ActionId { get; set; } = string.Empty;

    public DailySocialTimeSlot TimeSlot { get; set; }

    public double Score { get; set; }

    public int SelectedOrder { get; set; }

    public List<string> ReasonTags { get; set; } = new();

    public DailySocialCandidateStatus Status { get; set; } = DailySocialCandidateStatus.Planned;

    public void Normalize()
    {
        NpcName = SocialModelNormalization.LimitSingleLine(NpcName, 80);
        ActionId = SocialModelNormalization.LimitSingleLine(ActionId, 128);
        Score = SocialModelNormalization.ClampFinite(Score, 0d, 1d);
        SelectedOrder = Math.Max(0, SelectedOrder);
        ReasonTags = SocialModelNormalization.NormalizeTokens(ReasonTags, 8, 64);
        if (!Enum.IsDefined(typeof(DailySocialTimeSlot), TimeSlot))
            TimeSlot = DailySocialTimeSlot.Morning;
        if (!Enum.IsDefined(typeof(DailySocialCandidateStatus), Status))
            Status = DailySocialCandidateStatus.Cancelled;
    }
}

/// <summary>Recent relationship signals for one player/NPC pair.</summary>
public sealed class NpcSocialState
{
    public const int MaxRecentSignals = 12;

    public const int MaxRecentGiftHistory = 32;

    public string NpcName { get; set; } = string.Empty;

    public List<ConversationSignal> RecentSignals { get; set; } = new();

    public int LastConversationDay { get; set; } = -1;

    public int LastProactiveDay { get; set; } = -1;

    public int LastGiftOfferDay { get; set; } = -1;

    public int LastGiftDay { get; set; } = -1;

    /// <summary>Most recent day on which the player gave this NPC a vanilla gift.</summary>
    public int LastPlayerGiftDay { get; set; } = -1;

    public List<NpcGiftHistoryEntry> RecentGifts { get; set; } = new();

    /// <summary>Idempotency ledger. This is deliberately not truncated during normalization.</summary>
    public HashSet<string> CompletedActionIds { get; set; } = new(StringComparer.Ordinal);

    public void Normalize(string npcName)
    {
        NpcName = SocialModelNormalization.RequireIdentifier(npcName, nameof(npcName));
        LastConversationDay = Math.Max(-1, LastConversationDay);
        LastProactiveDay = Math.Max(-1, LastProactiveDay);
        LastGiftOfferDay = Math.Max(-1, LastGiftOfferDay);
        LastGiftDay = Math.Max(-1, LastGiftDay);
        LastPlayerGiftDay = Math.Max(-1, LastPlayerGiftDay);

        RecentSignals = (RecentSignals ?? new List<ConversationSignal>())
            .Where(signal => signal is not null)
            .Select(signal => signal.CloneNormalized())
            .OrderByDescending(signal => signal.Day)
            .ThenByDescending(signal => signal.ConversationTurn)
            .Take(MaxRecentSignals)
            .OrderBy(signal => signal.Day)
            .ThenBy(signal => signal.ConversationTurn)
            .ToList();

        if (RecentSignals.Count > 0)
            LastConversationDay = Math.Max(LastConversationDay, RecentSignals[^1].Day);

        RecentGifts = (RecentGifts ?? new List<NpcGiftHistoryEntry>())
            .Where(gift => gift is not null && !string.IsNullOrWhiteSpace(gift.QualifiedItemId) && gift.Day >= 0)
            .Select(gift => new NpcGiftHistoryEntry
            {
                QualifiedItemId = SocialModelNormalization.LimitSingleLine(gift.QualifiedItemId, 80),
                Day = gift.Day,
            })
            .GroupBy(gift => new { gift.QualifiedItemId, gift.Day })
            .Select(group => group.First())
            .OrderByDescending(gift => gift.Day)
            .Take(MaxRecentGiftHistory)
            .OrderBy(gift => gift.Day)
            .ToList();

        CompletedActionIds = new HashSet<string>(
            (CompletedActionIds ?? new HashSet<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => SocialModelNormalization.LimitSingleLine(value, 128))
            .Where(value => value.Length > 0),
            StringComparer.Ordinal);
    }

    public ConversationSignal? GetLatestSignal()
        => RecentSignals?
            .Where(signal => signal is not null)
            .OrderByDescending(signal => signal.Day)
            .ThenByDescending(signal => signal.ConversationTurn)
            .FirstOrDefault();

    internal void MergeFrom(NpcSocialState other)
    {
        LastConversationDay = Math.Max(LastConversationDay, other.LastConversationDay);
        LastProactiveDay = Math.Max(LastProactiveDay, other.LastProactiveDay);
        LastGiftOfferDay = Math.Max(LastGiftOfferDay, other.LastGiftOfferDay);
        LastGiftDay = Math.Max(LastGiftDay, other.LastGiftDay);
        LastPlayerGiftDay = Math.Max(LastPlayerGiftDay, other.LastPlayerGiftDay);
        RecentSignals.AddRange(other.RecentSignals ?? new List<ConversationSignal>());
        RecentGifts.AddRange(other.RecentGifts ?? new List<NpcGiftHistoryEntry>());
        CompletedActionIds.UnionWith(other.CompletedActionIds ?? new HashSet<string>());
        Normalize(NpcName);
    }

    public void RecordGift(string qualifiedItemId, int day)
    {
        string itemId = SocialModelNormalization.LimitSingleLine(qualifiedItemId, 80);
        if (itemId.Length == 0 || day < 0)
            return;

        RecentGifts ??= new List<NpcGiftHistoryEntry>();
        RecentGifts.Add(new NpcGiftHistoryEntry { QualifiedItemId = itemId, Day = day });
        Normalize(NpcName);
    }
}

public sealed class NpcGiftHistoryEntry
{
    public string QualifiedItemId { get; set; } = string.Empty;

    public int Day { get; set; }
}

/// <summary>Small structured result extracted from one completed AI conversation.</summary>
public sealed class ConversationSignal
{
    public const int MaxTopics = 8;
    public const int MaxOpenLoops = 6;

    public int Day { get; set; } = -1;

    public long ConversationTurn { get; set; }

    /// <summary>Conversation sentiment in the inclusive range -1..1.</summary>
    public double Valence { get; set; }

    public double Warmth { get; set; }

    public double Concern { get; set; }

    public double Confidence { get; set; }

    public List<string> Topics { get; set; } = new();

    public List<string> OpenLoops { get; set; } = new();

    public void Normalize()
    {
        Day = Math.Max(-1, Day);
        ConversationTurn = Math.Max(0, ConversationTurn);
        Valence = SocialModelNormalization.ClampFinite(Valence, -1d, 1d);
        Warmth = SocialModelNormalization.ClampFinite(Warmth, 0d, 1d);
        Concern = SocialModelNormalization.ClampFinite(Concern, 0d, 1d);
        Confidence = SocialModelNormalization.ClampFinite(Confidence, 0d, 1d);
        Topics = SocialModelNormalization.NormalizeTokens(Topics, MaxTopics, 64);
        OpenLoops = SocialModelNormalization.NormalizeTokens(OpenLoops, MaxOpenLoops, 96);
    }

    public ConversationSignal CloneNormalized()
    {
        var clone = new ConversationSignal
        {
            Day = Day,
            ConversationTurn = ConversationTurn,
            Valence = Valence,
            Warmth = Warmth,
            Concern = Concern,
            Confidence = Confidence,
            Topics = new List<string>(Topics ?? new List<string>()),
            OpenLoops = new List<string>(OpenLoops ?? new List<string>()),
        };
        clone.Normalize();
        return clone;
    }

    public double GetPositiveScore()
    {
        double normalizedValence = (SocialModelNormalization.ClampFinite(Valence, -1d, 1d) + 1d) / 2d;
        double warmth = SocialModelNormalization.ClampFinite(Warmth, 0d, 1d);
        return (normalizedValence * 0.65d) + (warmth * 0.35d);
    }
}

/// <summary>A bounded per-day aggregate of player activity tags.</summary>
public sealed class DailyActivitySummary
{
    public const int MaxTags = 32;
    public const int MaxTagCount = 999;

    public int Day { get; set; } = -1;

    public Dictionary<string, int> ActivityTags { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string tag, int count = 1)
    {
        tag = SocialModelNormalization.LimitSingleLine(tag, 64);
        if (tag.Length == 0 || count <= 0)
            return;

        ActivityTags ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int current = ActivityTags.TryGetValue(tag, out int value) ? value : 0;
        ActivityTags[tag] = (int)Math.Clamp((long)current + count, 0L, MaxTagCount);
        Normalize();
    }

    public void Normalize()
    {
        Day = Math.Max(-1, Day);
        ActivityTags = (ActivityTags ?? new Dictionary<string, int>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .Select(pair => new KeyValuePair<string, int>(
                SocialModelNormalization.LimitSingleLine(pair.Key, 64),
                Math.Clamp(pair.Value, 1, MaxTagCount)))
            .Where(pair => pair.Key.Length > 0)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new KeyValuePair<string, int>(
                group.Key,
                (int)Math.Min(MaxTagCount, group.Sum(pair => (long)pair.Value))))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    public static DailyActivitySummary Merge(IEnumerable<DailyActivitySummary> summaries)
    {
        DailyActivitySummary[] values = (summaries ?? Array.Empty<DailyActivitySummary>())
            .Where(summary => summary is not null)
            .ToArray();
        var merged = new DailyActivitySummary { Day = values.Length == 0 ? -1 : values.Max(value => value.Day) };
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (DailyActivitySummary summary in values)
        {
            foreach ((string tag, int count) in summary.ActivityTags ?? new Dictionary<string, int>())
            {
                string normalizedTag = SocialModelNormalization.LimitSingleLine(tag, 64);
                if (normalizedTag.Length == 0 || count <= 0)
                    continue;

                long current = totals.TryGetValue(normalizedTag, out long value) ? value : 0L;
                totals[normalizedTag] = Math.Min(MaxTagCount, current + count);
            }
        }

        merged.ActivityTags = totals.ToDictionary(
            pair => pair.Key,
            pair => (int)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        merged.Normalize();
        return merged;
    }
}

/// <summary>Pure input snapshot used by <see cref="Services.DailySocialPlanner"/>.</summary>
public sealed class SocialPlanningCandidate
{
    public string NpcName { get; set; } = string.Empty;

    public bool ExistsInSave { get; set; } = true;

    public bool CanSocialize { get; set; } = true;

    public bool RelationshipBlocked { get; set; }

    public int VanillaHearts { get; set; }

    public int LastConversationDay { get; set; } = -1;

    public int LastProactiveDay { get; set; } = -1;

    public int LastPlayerGiftDay { get; set; } = -1;

    public List<ConversationSignal> RecentSignals { get; set; } = new();

    public static SocialPlanningCandidate FromState(
        NpcSocialState state,
        int vanillaHearts,
        bool existsInSave = true,
        bool canSocialize = true,
        bool relationshipBlocked = false)
    {
        if (state is null)
            throw new ArgumentNullException(nameof(state));

        return new SocialPlanningCandidate
        {
            NpcName = state.NpcName ?? string.Empty,
            ExistsInSave = existsInSave,
            CanSocialize = canSocialize,
            RelationshipBlocked = relationshipBlocked,
            VanillaHearts = vanillaHearts,
            LastConversationDay = state.LastConversationDay,
            LastProactiveDay = state.LastProactiveDay,
            LastPlayerGiftDay = state.LastPlayerGiftDay,
            RecentSignals = (state.RecentSignals ?? new List<ConversationSignal>())
                .Where(signal => signal is not null)
                .Select(signal => signal.CloneNormalized())
                .ToList(),
        };
    }
}

public sealed class SocialCandidateEvaluation
{
    public string NpcName { get; init; } = string.Empty;

    public bool IsEligible { get; init; }

    public string ExclusionReason { get; init; } = string.Empty;

    public double Score { get; init; }

    public int LastPlayerGiftDay { get; init; } = -1;

    public IReadOnlyList<string> ReasonTags { get; init; } = Array.Empty<string>();
}

public sealed class DailySocialPlannerOptions
{
    public const int AbsoluteMaximumCandidates = 16;

    public int PlannerVersion { get; set; } = 1;

    public int MinimumCandidates { get; set; } = 3;

    public int MaximumCandidates { get; set; } = 5;

    public int ConversationLookbackDays { get; set; } = 14;

    /// <summary>Legacy setting retained for old callers. Daily selection no longer applies a cooldown.</summary>
    public int ProactiveCooldownDays { get; set; }

    public double MinimumPositiveScore { get; set; } = 0.55d;

    public double LatestNegativeValenceThreshold { get; set; } = -0.25d;

    public double RecencyWeight { get; set; } = 0.30d;

    public double PositivityWeight { get; set; } = 0.30d;

    public double ConcernWeight { get; set; } = 0.15d;

    public double HeartsWeight { get; set; } = 0.10d;

    public double DormancyWeight { get; set; } = 0.15d;

    /// <summary>False for controller mode, where manual AI chat history isn't available.</summary>
    public bool RequireRecentPositiveConversation { get; set; } = true;

    public bool PrioritizeRecentPlayerGifts { get; set; }

    public bool ControllerMode { get; set; }

    public DailySocialPlannerOptions Normalize()
    {
        var normalized = new DailySocialPlannerOptions
        {
            PlannerVersion = Math.Max(1, PlannerVersion),
            MinimumCandidates = Math.Clamp(MinimumCandidates, 0, AbsoluteMaximumCandidates),
            MaximumCandidates = Math.Clamp(MaximumCandidates, 0, AbsoluteMaximumCandidates),
            ConversationLookbackDays = Math.Clamp(ConversationLookbackDays, 1, 112),
            ProactiveCooldownDays = Math.Clamp(ProactiveCooldownDays, 0, 112),
            MinimumPositiveScore = SocialModelNormalization.ClampFinite(MinimumPositiveScore, 0d, 1d),
            LatestNegativeValenceThreshold = SocialModelNormalization.ClampFinite(
                LatestNegativeValenceThreshold,
                -1d,
                1d),
            RecencyWeight = SocialModelNormalization.ClampFinite(RecencyWeight, 0d, 100d),
            PositivityWeight = SocialModelNormalization.ClampFinite(PositivityWeight, 0d, 100d),
            ConcernWeight = SocialModelNormalization.ClampFinite(ConcernWeight, 0d, 100d),
            HeartsWeight = SocialModelNormalization.ClampFinite(HeartsWeight, 0d, 100d),
            DormancyWeight = SocialModelNormalization.ClampFinite(DormancyWeight, 0d, 100d),
            RequireRecentPositiveConversation = RequireRecentPositiveConversation,
            PrioritizeRecentPlayerGifts = PrioritizeRecentPlayerGifts,
            ControllerMode = ControllerMode,
        };

        if (normalized.MaximumCandidates < normalized.MinimumCandidates)
            normalized.MaximumCandidates = normalized.MinimumCandidates;

        double totalWeight = normalized.RecencyWeight
                             + normalized.PositivityWeight
                             + normalized.ConcernWeight
                             + normalized.HeartsWeight
                             + normalized.DormancyWeight;
        if (totalWeight <= 0d)
        {
            normalized.RecencyWeight = 0.30d;
            normalized.PositivityWeight = 0.30d;
            normalized.ConcernWeight = 0.15d;
            normalized.HeartsWeight = 0.10d;
            normalized.DormancyWeight = 0.15d;
        }

        return normalized;
    }

    public IReadOnlyList<string> Validate()
    {
        var issues = new List<string>();
        if (PlannerVersion < 1)
            issues.Add("plannerVersion must be at least 1.");
        if (MinimumCandidates is < 0 or > AbsoluteMaximumCandidates)
            issues.Add($"minimumCandidates must be between 0 and {AbsoluteMaximumCandidates}.");
        if (MaximumCandidates < MinimumCandidates || MaximumCandidates > AbsoluteMaximumCandidates)
            issues.Add($"maximumCandidates must be between minimumCandidates and {AbsoluteMaximumCandidates}.");
        if (ConversationLookbackDays is < 1 or > 112)
            issues.Add("conversationLookbackDays must be between 1 and 112.");
        if (ProactiveCooldownDays is < 0 or > 112)
            issues.Add("proactiveCooldownDays must be between 0 and 112.");
        if (!SocialModelNormalization.IsFinite(MinimumPositiveScore)
            || MinimumPositiveScore is < 0d or > 1d)
        {
            issues.Add("minimumPositiveScore must be between 0 and 1.");
        }
        if (!SocialModelNormalization.IsFinite(LatestNegativeValenceThreshold)
            || LatestNegativeValenceThreshold is < -1d or > 1d)
        {
            issues.Add("latestNegativeValenceThreshold must be between -1 and 1.");
        }

        double[] weights = { RecencyWeight, PositivityWeight, ConcernWeight, HeartsWeight, DormancyWeight };
        if (weights.Any(weight => !SocialModelNormalization.IsFinite(weight) || weight < 0d))
            issues.Add("planner weights must be finite and non-negative.");
        else if (weights.Sum() <= 0d)
            issues.Add("at least one planner weight must be positive.");
        return issues;
    }
}

internal static class SocialModelNormalization
{
    public static string RequireIdentifier(string? value, string parameterName)
    {
        string normalized = NormalizeIdentifier(value);
        if (normalized.Length == 0)
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        return normalized;
    }

    public static string NormalizeIdentifier(string? value)
        => LimitSingleLine(value, 128);

    public static string LimitSingleLine(string? value, int maximumLength)
    {
        if (maximumLength < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));

        string normalized = string.Join(
            " ",
            (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    public static List<string> NormalizeTokens(IEnumerable<string>? values, int maximumCount, int maximumLength)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return (values ?? Array.Empty<string>())
            .Select(value => LimitSingleLine(value, maximumLength))
            .Where(value => value.Length > 0 && seen.Add(value))
            .Take(Math.Max(0, maximumCount))
            .ToList();
    }

    public static double ClampFinite(double value, double minimum, double maximum)
        => !IsFinite(value) ? minimum : Math.Clamp(value, minimum, maximum);

    public static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
