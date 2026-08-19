using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VivantValley.Services;

/// <summary>
/// Pure deterministic planner for daily proactive NPC opportunities. Callers own
/// persistence and provide game-derived snapshots through <see cref="SocialPlanningCandidate"/>.
/// </summary>
public sealed class DailySocialPlanner
{
    // Stable across the Vivant Valley rename so existing saves keep deterministic daily plans.
    private const string SeedDomain = "StardewAIMemories.DailySocialPlanner";
    private const double MinimumSamplingWeight = 0.000001d;

    public DailySocialPlan CreatePlan(
        string saveId,
        string playerId,
        int day,
        IEnumerable<SocialPlanningCandidate> candidates,
        DailySocialPlannerOptions? options = null)
    {
        saveId = RequireSeedIdentifier(saveId, nameof(saveId));
        playerId = RequireSeedIdentifier(playerId, nameof(playerId));
        if (day < 0)
            throw new ArgumentOutOfRangeException(nameof(day), "Day must be non-negative.");
        if (candidates is null)
            throw new ArgumentNullException(nameof(candidates));

        DailySocialPlannerOptions normalizedOptions = (options ?? new DailySocialPlannerOptions()).Normalize();
        byte[] seedBytes = ComputeSeedBytes(
            saveId,
            playerId,
            day,
            normalizedOptions.PlannerVersion);
        string seed = Convert.ToHexString(seedBytes).ToLowerInvariant();

        List<SocialCandidateEvaluation> eligible = candidates
            .Where(candidate => candidate is not null)
            .Select(candidate => EvaluateCandidateCore(candidate, day, normalizedOptions))
            .Where(evaluation => evaluation.IsEligible)
            .GroupBy(evaluation => evaluation.NpcName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(evaluation => evaluation.Score)
                .ThenBy(evaluation => evaluation.NpcName, StringComparer.Ordinal)
                .ThenBy(
                    evaluation => string.Join("\u001f", evaluation.ReasonTags),
                    StringComparer.Ordinal)
                .First())
            .OrderBy(evaluation => evaluation.NpcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(evaluation => evaluation.NpcName, StringComparer.Ordinal)
            .ToList();

        var plan = new DailySocialPlan
        {
            Day = day,
            PlannerVersion = normalizedOptions.PlannerVersion,
            Seed = seed,
        };

        if (eligible.Count == 0 || normalizedOptions.MaximumCandidates == 0)
            return plan;

        var random = new DeterministicRandom(seedBytes);
        int upperBound = Math.Min(normalizedOptions.MaximumCandidates, eligible.Count);
        int lowerBound = Math.Min(normalizedOptions.MinimumCandidates, upperBound);
        int selectionCount = lowerBound == upperBound
            ? upperBound
            : random.NextInt32(lowerBound, checked(upperBound + 1));

        List<SocialCandidateEvaluation> selected = SampleWithoutReplacement(eligible, selectionCount, random);
        for (int index = 0; index < selected.Count; index++)
        {
            SocialCandidateEvaluation evaluation = selected[index];
            foreach (DailySocialTimeSlot timeSlot in Enum.GetValues<DailySocialTimeSlot>())
            {
                plan.Candidates.Add(new DailySocialCandidate
                {
                    NpcName = evaluation.NpcName,
                    ActionId = CreateActionId(seed, day, evaluation.NpcName, timeSlot),
                    TimeSlot = timeSlot,
                    Score = Math.Round(evaluation.Score, 6, MidpointRounding.AwayFromZero),
                    SelectedOrder = index,
                    ReasonTags = evaluation.ReasonTags.ToList(),
                    Status = DailySocialCandidateStatus.Planned,
                });
            }
        }

        plan.Normalize();
        return plan;
    }

    /// <summary>Alias for callers that describe daily planning as a build operation.</summary>
    public DailySocialPlan BuildPlan(
        string saveId,
        string playerId,
        int day,
        IEnumerable<SocialPlanningCandidate> candidates,
        DailySocialPlannerOptions? options = null)
        => CreatePlan(saveId, playerId, day, candidates, options);

    public SocialCandidateEvaluation EvaluateCandidate(
        SocialPlanningCandidate candidate,
        int day,
        DailySocialPlannerOptions? options = null)
    {
        if (candidate is null)
            throw new ArgumentNullException(nameof(candidate));
        if (day < 0)
            throw new ArgumentOutOfRangeException(nameof(day), "Day must be non-negative.");

        return EvaluateCandidateCore(candidate, day, (options ?? new DailySocialPlannerOptions()).Normalize());
    }

    public IReadOnlyList<SocialCandidateEvaluation> EvaluateCandidates(
        IEnumerable<SocialPlanningCandidate> candidates,
        int day,
        DailySocialPlannerOptions? options = null)
    {
        if (candidates is null)
            throw new ArgumentNullException(nameof(candidates));
        if (day < 0)
            throw new ArgumentOutOfRangeException(nameof(day), "Day must be non-negative.");

        DailySocialPlannerOptions normalizedOptions = (options ?? new DailySocialPlannerOptions()).Normalize();
        return candidates
            .Where(candidate => candidate is not null)
            .Select(candidate => EvaluateCandidateCore(candidate, day, normalizedOptions))
            .OrderByDescending(evaluation => evaluation.IsEligible)
            .ThenByDescending(evaluation => evaluation.Score)
            .ThenBy(evaluation => evaluation.NpcName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Computes the stable seed persisted with a plan. The length-prefixed input avoids
    /// collisions caused by delimiter characters inside save or player identifiers.
    /// </summary>
    public static string ComputeSeed(string saveId, string playerId, int day, int plannerVersion)
    {
        saveId = RequireSeedIdentifier(saveId, nameof(saveId));
        playerId = RequireSeedIdentifier(playerId, nameof(playerId));
        if (day < 0)
            throw new ArgumentOutOfRangeException(nameof(day), "Day must be non-negative.");
        if (plannerVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(plannerVersion), "Planner version must be at least 1.");

        return Convert.ToHexString(ComputeSeedBytes(saveId, playerId, day, plannerVersion)).ToLowerInvariant();
    }

    public static bool IsCurrentPlan(DailySocialPlan? plan, int day, int plannerVersion)
        => plan is not null
           && day >= 0
           && plannerVersion >= 1
           && plan.Day == day
           && plan.PlannerVersion == plannerVersion
           && plan.Seed?.Length == 64
           && plan.Seed.All(Uri.IsHexDigit);

    private static SocialCandidateEvaluation EvaluateCandidateCore(
        SocialPlanningCandidate candidate,
        int day,
        DailySocialPlannerOptions options)
    {
        string npcName = SocialModelNormalization.LimitSingleLine(candidate.NpcName, 80);
        if (npcName.Length == 0)
            return Excluded(string.Empty, "invalid_npc_name");
        if (!candidate.ExistsInSave)
            return Excluded(npcName, "not_in_save");
        if (!candidate.CanSocialize)
            return Excluded(npcName, "not_socializable");
        if (candidate.RelationshipBlocked)
            return Excluded(npcName, "relationship_blocked");

        List<ConversationSignal> recentSignals = (candidate.RecentSignals ?? new List<ConversationSignal>())
            .Where(signal => signal is not null)
            .Select(signal => signal.CloneNormalized())
            .Where(signal => signal.Day >= 0 && signal.Day <= day)
            .OrderByDescending(signal => signal.Day)
            .ThenByDescending(signal => signal.ConversationTurn)
            .ToList();

        if (candidate.LastConversationDay > day)
            return Excluded(npcName, "conversation_in_future");
        if (candidate.LastProactiveDay > day)
            return Excluded(npcName, "proactive_day_in_future");

        int lastConversationDay = candidate.LastConversationDay;
        if (recentSignals.Count > 0)
            lastConversationDay = Math.Max(lastConversationDay, recentSignals[0].Day);
        if (lastConversationDay < 0)
            return Excluded(npcName, "never_conversed");

        int conversationAge = day - lastConversationDay;
        if (conversationAge > options.ConversationLookbackDays)
            return Excluded(npcName, "conversation_too_old");

        ConversationSignal[] scoringSignals = recentSignals
            .Where(signal => day - signal.Day <= options.ConversationLookbackDays)
            .Take(3)
            .ToArray();
        if (scoringSignals.Length == 0)
            return Excluded(npcName, "no_recent_signal");
        if (scoringSignals[0].Valence < options.LatestNegativeValenceThreshold)
            return Excluded(npcName, "latest_conversation_negative");

        double positivity = WeightedAverage(
            scoringSignals,
            signal => signal.GetPositiveScore(),
            signal => 0.25d + (signal.Confidence * 0.75d));
        if (positivity < options.MinimumPositiveScore)
            return Excluded(npcName, "not_positive_enough");

        double recency = 1d - Math.Clamp(
            conversationAge / (double)options.ConversationLookbackDays,
            0d,
            1d);
        bool hasOpenLoop = scoringSignals.Any(signal => signal.OpenLoops.Count > 0);
        double concern = Math.Max(
            WeightedAverage(
                scoringSignals,
                signal => signal.Concern,
                signal => 0.25d + (signal.Confidence * 0.75d)),
            hasOpenLoop ? 0.75d : 0d);
        double hearts = Math.Clamp(candidate.VanillaHearts, 0, 14) / 14d;
        double dormancy = candidate.LastProactiveDay < 0
            ? 1d
            : Math.Clamp(
                (day - candidate.LastProactiveDay) / (double)options.ConversationLookbackDays,
                0d,
                1d);

        double totalWeight = options.RecencyWeight
                             + options.PositivityWeight
                             + options.ConcernWeight
                             + options.HeartsWeight
                             + options.DormancyWeight;
        double score = ((recency * options.RecencyWeight)
                        + (positivity * options.PositivityWeight)
                        + (concern * options.ConcernWeight)
                        + (hearts * options.HeartsWeight)
                        + (dormancy * options.DormancyWeight))
                       / totalWeight;

        var reasonTags = new List<string> { "recent_positive_conversation" };
        if (hasOpenLoop)
            reasonTags.Add("open_loop");
        else if (concern >= 0.5d)
            reasonTags.Add("concern");
        if (hearts >= 0.5d)
            reasonTags.Add("established_relationship");
        if (candidate.LastProactiveDay < 0)
            reasonTags.Add("never_proactive");
        else if (dormancy >= 0.5d)
            reasonTags.Add("long_since_proactive");

        return new SocialCandidateEvaluation
        {
            NpcName = npcName,
            IsEligible = true,
            Score = SocialModelNormalization.ClampFinite(score, 0d, 1d),
            ReasonTags = reasonTags,
        };
    }

    private static SocialCandidateEvaluation Excluded(string npcName, string reason)
        => new()
        {
            NpcName = npcName,
            IsEligible = false,
            ExclusionReason = reason,
        };

    private static double WeightedAverage(
        IReadOnlyList<ConversationSignal> signals,
        Func<ConversationSignal, double> valueSelector,
        Func<ConversationSignal, double> confidenceSelector)
    {
        double weightedTotal = 0d;
        double totalWeight = 0d;
        for (int index = 0; index < signals.Count; index++)
        {
            // The latest signal is strongest; confidence adjusts but never erases it.
            double recencyWeight = Math.Pow(0.65d, index);
            double weight = recencyWeight * Math.Clamp(confidenceSelector(signals[index]), 0.01d, 1d);
            weightedTotal += SocialModelNormalization.ClampFinite(valueSelector(signals[index]), 0d, 1d) * weight;
            totalWeight += weight;
        }

        return totalWeight <= 0d ? 0d : weightedTotal / totalWeight;
    }

    private static List<SocialCandidateEvaluation> SampleWithoutReplacement(
        IReadOnlyCollection<SocialCandidateEvaluation> candidates,
        int count,
        DeterministicRandom random)
    {
        var remaining = candidates
            .OrderBy(candidate => candidate.NpcName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.NpcName, StringComparer.Ordinal)
            .ToList();
        var selected = new List<SocialCandidateEvaluation>(Math.Min(count, remaining.Count));

        while (selected.Count < count && remaining.Count > 0)
        {
            double totalWeight = remaining.Sum(candidate => Math.Max(MinimumSamplingWeight, candidate.Score));
            double target = random.NextDouble() * totalWeight;
            int selectedIndex = remaining.Count - 1;
            for (int index = 0; index < remaining.Count; index++)
            {
                target -= Math.Max(MinimumSamplingWeight, remaining[index].Score);
                if (target < 0d)
                {
                    selectedIndex = index;
                    break;
                }
            }

            selected.Add(remaining[selectedIndex]);
            remaining.RemoveAt(selectedIndex);
        }

        return selected;
    }

    private static string CreateActionId(
        string seed,
        int day,
        string npcName,
        DailySocialTimeSlot timeSlot)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(
            string.Concat(
                seed,
                "|",
                day.ToString(CultureInfo.InvariantCulture),
                "|",
                npcName,
                "|",
                timeSlot.ToString().ToLowerInvariant()));
        byte[] digest = SHA256.HashData(bytes);
        return $"social-{day}-{timeSlot.ToString().ToLowerInvariant()}-"
               + Convert.ToHexString(digest.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static byte[] ComputeSeedBytes(string saveId, string playerId, int day, int plannerVersion)
    {
        var payload = new List<byte>();
        AppendSeedPart(payload, SeedDomain);
        AppendSeedPart(payload, saveId);
        AppendSeedPart(payload, playerId);
        AppendSeedPart(payload, day.ToString(CultureInfo.InvariantCulture));
        AppendSeedPart(payload, plannerVersion.ToString(CultureInfo.InvariantCulture));
        return SHA256.HashData(payload.ToArray());
    }

    private static string RequireSeedIdentifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        return value.Trim();
    }

    private static void AppendSeedPart(List<byte> payload, string value)
    {
        byte[] encoded = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, encoded.Length);
        payload.AddRange(length.ToArray());
        payload.AddRange(encoded);
    }

    /// <summary>xoshiro256** with state initialized directly from the SHA-256 digest.</summary>
    private sealed class DeterministicRandom
    {
        private ulong state0;
        private ulong state1;
        private ulong state2;
        private ulong state3;

        public DeterministicRandom(ReadOnlySpan<byte> seed)
        {
            if (seed.Length < 32)
                throw new ArgumentException("A 32-byte seed is required.", nameof(seed));

            state0 = BinaryPrimitives.ReadUInt64BigEndian(seed[..8]);
            state1 = BinaryPrimitives.ReadUInt64BigEndian(seed.Slice(8, 8));
            state2 = BinaryPrimitives.ReadUInt64BigEndian(seed.Slice(16, 8));
            state3 = BinaryPrimitives.ReadUInt64BigEndian(seed.Slice(24, 8));
            if ((state0 | state1 | state2 | state3) == 0UL)
                state0 = 0x9E3779B97F4A7C15UL;
        }

        public int NextInt32(int minimumInclusive, int maximumExclusive)
        {
            if (minimumInclusive > maximumExclusive)
                throw new ArgumentOutOfRangeException(nameof(minimumInclusive));
            if (minimumInclusive == maximumExclusive)
                return minimumInclusive;

            uint range = checked((uint)(maximumExclusive - minimumInclusive));
            ulong threshold = unchecked((0UL - range) % range);
            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value < threshold);

            return checked(minimumInclusive + (int)(value % range));
        }

        public double NextDouble()
            => (NextUInt64() >> 11) * (1d / 9007199254740992d);

        private ulong NextUInt64()
        {
            ulong result = RotateLeft(state1 * 5UL, 7) * 9UL;
            ulong temporary = state1 << 17;

            state2 ^= state0;
            state3 ^= state1;
            state1 ^= state2;
            state0 ^= state3;
            state2 ^= temporary;
            state3 = RotateLeft(state3, 45);
            return result;
        }

        private static ulong RotateLeft(ulong value, int offset)
            => (value << offset) | (value >> (64 - offset));
    }
}
