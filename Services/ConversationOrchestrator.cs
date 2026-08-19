using System.Text;

namespace VivantValley.Services;

/// <summary>Builds one real LangGraph request for a manual NPC conversation.</summary>
public sealed class ConversationOrchestrator
{
    private readonly LangGraphClient client;
    private readonly Func<LangGraphBridgeAccess?> bridgeAccess;

    public ConversationOrchestrator(
        LangGraphClient client,
        Func<LangGraphBridgeAccess?>? bridgeAccess = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.bridgeAccess = bridgeAccess ?? (() => null);
    }

    public Task<LangGraphResponse> DecideAsync(
        NpcContextSnapshot snapshot,
        AiRuntimeProfile profile,
        string requestId,
        int maxOutputTokens,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("requestId cannot be empty.", nameof(requestId));

        var request = new LangGraphRequest
        {
            RequestId = requestId.Trim(),
            PlayerId = snapshot.PlayerId,
            NpcName = snapshot.NpcName,
            Day = snapshot.Day,
            Location = snapshot.Location,
            ActionId = snapshot.ActionId,
            ContextVersion = snapshot.ContextVersion,
            Mode = snapshot.Mode,
            ContextSnapshot = snapshot,
            GameBridge = bridgeAccess(),
            Llm = new LangGraphLlmProfile
            {
                Provider = profile.Provider,
                BaseUrl = profile.BaseUrl,
                Model = profile.Model,
                ApiKey = profile.ApiKey,
                EnableThinking = profile.EnableThinking,
                ReasoningEffort = profile.ReasoningEffort,
                MaxOutputTokens = Math.Clamp(maxOutputTokens, 128, 32768),
            },
        };
        return client.DecideAsync(request, cancellationToken);
    }
}

/// <summary>Validates untrusted graph output before any game-side action is executed.</summary>
public sealed class DecisionValidator
{
    public LangGraphDecision Validate(
        LangGraphResponse response,
        NpcContextSnapshot requestSnapshot,
        int maximumReplyCharacters,
        string? expectedRequestId = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(requestSnapshot);
        if (response.Decision is null)
            throw new LangGraphValidationException("Graph response is missing decision.");
        if (!string.IsNullOrWhiteSpace(expectedRequestId)
            && !string.Equals(response.RequestId, expectedRequestId, StringComparison.Ordinal))
        {
            throw new LangGraphValidationException("Graph response request ID does not match the active request.");
        }
        if (!string.IsNullOrWhiteSpace(response.ContextVersion)
            && !response.ContextVersion.Equals(requestSnapshot.ContextVersion, StringComparison.Ordinal))
        {
            throw new LangGraphValidationException("Graph response context version is stale.");
        }

        LangGraphDecision decision = response.Decision;
        if (decision.SchemaVersion != 1)
            throw new LangGraphValidationException("Unsupported graph decision schema version.");
        if (!decision.Decision.Equals("reply", StringComparison.OrdinalIgnoreCase))
            throw new LangGraphValidationException("Graph decision must be reply.");

        LangGraphAction action = decision.Action ?? new LangGraphAction();
        action.Name = NormalizeAction(action.Name);
        action.CandidateKey = NormalizeOptional(action.CandidateKey, 128);
        action.Delivery = NormalizeOptional(action.Delivery, 32) ?? SocialGiftDeliveryModes.Immediate;
        action.ReasonTag = NormalizeOptional(action.ReasonTag, 64) ?? string.Empty;
        if (action.Name is not (NpcGiftToolNames.None or NpcGiftToolNames.GiveGift or NpcGiftToolNames.MailGift))
            throw new LangGraphValidationException("Graph returned an unknown tool name.");
        if (action.Name == NpcGiftToolNames.None && action.CandidateKey is not null)
            throw new LangGraphValidationException("none action cannot contain a candidate key.");
        if (action.Name != NpcGiftToolNames.None && action.CandidateKey is null)
            throw new LangGraphValidationException("Gift action is missing candidate key.");
        if (!action.Delivery.Equals(SocialGiftDeliveryModes.Immediate, StringComparison.Ordinal)
            && !action.Delivery.Equals(SocialGiftDeliveryModes.Mail, StringComparison.Ordinal))
        {
            throw new LangGraphValidationException("Graph returned an unknown delivery mode.");
        }
        if (action.Name != NpcGiftToolNames.None
            && !(requestSnapshot.AllowedTools ?? Array.Empty<LangGraphGiftCandidate>()).Any(candidate => string.Equals(
                candidate.CandidateKey,
                action.CandidateKey,
                StringComparison.Ordinal)))
        {
            throw new LangGraphValidationException("Graph selected a candidate outside the current allowlist.");
        }

        decision.Reply = NormalizeReply(decision.Reply, maximumReplyCharacters);
        if (decision.Reply.Length == 0)
            throw new LangGraphValidationException("Graph returned an empty reply.");
        if (ContainsForbiddenReplyContent(decision.Reply))
            throw new LangGraphValidationException("Graph reply contains JSON, tool syntax, or game control characters.");

        LangGraphMemoryUpdate update = decision.MemoryUpdate ?? new LangGraphMemoryUpdate();
        update.SummaryPatch = LimitSingleLine(update.SummaryPatch, 1800);
        update.Topics = NormalizeTokens(update.Topics, ConversationSignal.MaxTopics, 64);
        update.OpenLoops = NormalizeTokens(update.OpenLoops, ConversationSignal.MaxOpenLoops, 96);
        update.Signal ??= new LangGraphSignal();
        update.Signal.Valence = ClampFinite(update.Signal.Valence, -1d, 1d);
        update.Signal.Warmth = ClampFinite(update.Signal.Warmth, 0d, 1d);
        update.Signal.Concern = ClampFinite(update.Signal.Concern, 0d, 1d);
        update.Signal.Confidence = ClampFinite(update.Signal.Confidence, 0d, 1d);
        decision.Action = action;
        decision.MemoryUpdate = update;
        return decision;
    }

    private static string NormalizeAction(string? value)
        => (value ?? NpcGiftToolNames.None).Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        string normalized = LimitSingleLine(value, maximumLength);
        return normalized.Length == 0 ? null : normalized;
    }

    private static string NormalizeReply(string? value, int maximumLength)
    {
        string normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', '\n').Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static bool ContainsForbiddenReplyContent(string value)
    {
        string trimmed = value.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal)
            || trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                using var _ = System.Text.Json.JsonDocument.Parse(trimmed);
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                // A normal sentence may begin with punctuation; only valid JSON is rejected here.
            }
        }

        return value.Contains("$", StringComparison.Ordinal)
               || value.Contains("#", StringComparison.Ordinal)
               || value.Contains("^", StringComparison.Ordinal)
               || value.Contains("%", StringComparison.Ordinal)
               || value.Contains("<tool", StringComparison.OrdinalIgnoreCase)
               || value.Contains("SMAPI", StringComparison.OrdinalIgnoreCase);
    }

    private static string LimitSingleLine(string? value, int maximumLength)
    {
        string normalized = string.Join(" ", (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static List<string> NormalizeTokens(IEnumerable<string>? values, int maximumCount, int maximumLength)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values ?? Array.Empty<string>())
        {
            string normalized = LimitSingleLine(value, maximumLength);
            if (normalized.Length > 0 && seen.Add(normalized))
                result.Add(normalized);
            if (result.Count >= maximumCount)
                break;
        }
        return result;
    }

    private static double ClampFinite(double value, double minimum, double maximum)
        => double.IsNaN(value) || double.IsInfinity(value) ? minimum : Math.Clamp(value, minimum, maximum);
}

public sealed class LangGraphValidationException : Exception
{
    public LangGraphValidationException(string message) : base(message)
    {
    }
}

public sealed class ToolRegistry
{
    private readonly HashSet<string> names = new(StringComparer.Ordinal)
    {
        NpcGiftToolNames.None,
        NpcGiftToolNames.GiveGift,
        NpcGiftToolNames.MailGift,
    };

    public bool Contains(string? name)
        => name is not null && names.Contains(name.Trim().ToLowerInvariant());
}

/// <summary>Normalizes a validated action before handing it to the SMAPI-thread executor.</summary>
public sealed class ToolRouter
{
    private readonly ToolRegistry registry;

    public ToolRouter(ToolRegistry registry)
    {
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public LangGraphAction Route(LangGraphAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!registry.Contains(action.Name))
            throw new LangGraphValidationException("Tool is not registered.");
        return action;
    }
}
