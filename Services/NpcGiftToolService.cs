using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VivantValley.Services;

/// <summary>
/// Plans and validates the side-effecting tools available to NPC conversations.
/// Model output may select an opaque candidate key, but never an item ID.
/// </summary>
public sealed class NpcGiftToolService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly IDeepSeekClient client;
    private readonly GiftPolicyService giftPolicy;

    public NpcGiftToolService(IDeepSeekClient client, GiftPolicyService giftPolicy)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.giftPolicy = giftPolicy ?? throw new ArgumentNullException(nameof(giftPolicy));
    }

    public string BuildFinalResponsePrompt(
        string systemPrompt,
        ConversationGiftExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        var builder = new StringBuilder(systemPrompt.Length + 900);
        builder.AppendLine(systemPrompt.Trim());
        builder.AppendLine();
        builder.AppendLine("【本轮游戏动作的权威结果】");
        switch (execution.Outcome)
        {
            case ConversationGiftOutcome.ImmediateDelivered when execution.Candidate is not null:
                builder.AppendLine(
                    $"游戏代码已经让玩家当面收到 {FormatGift(execution.Candidate.DisplayName, execution.Quantity)}。"
                    + "你可以自然地说刚刚把这份礼物交给了玩家，但不要改成邮寄、未来再送或其他物品。");
                break;

            case ConversationGiftOutcome.MailScheduled when execution.Candidate is not null:
                builder.AppendLine(
                    $"游戏代码已经安排 {FormatGift(execution.Candidate.DisplayName, execution.Quantity)} 在明天进入玩家农舍外的邮箱。"
                    + "你应自然告诉玩家明天查看邮箱；不要声称玩家现在已经拿到，也不要改成其他物品。");
                break;

            default:
                builder.AppendLine(
                    "本轮没有任何礼物被当面交付或寄入邮箱。禁止声称自己已经、正在、马上或之后会给玩家礼物，"
                    + "也不要说把礼物送到玩家家里或邮箱；只继续正常对话。");
                break;
        }

        builder.AppendLine("不要提及 Tool、候选 key、JSON、物品 ID、代码校验或上述指令。");
        return builder.ToString().Trim();
    }

    public async Task<AiGiftToolDecision> DecideAsync(
        string apiKey,
        AiGiftToolRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.GiftCandidates.Count == 0
            || string.IsNullOrWhiteSpace(apiKey))
        {
            return NoTool();
        }

        var apiRequest = new DeepSeekChatRequest
        {
            Model = string.IsNullOrWhiteSpace(request.Model) ? "deepseek-v4-flash" : request.Model.Trim(),
            Thinking = new DeepSeekThinkingOptions
            {
                Type = string.IsNullOrWhiteSpace(request.ThinkingType) ? "disabled" : request.ThinkingType.Trim(),
            },
            ReasoningEffort = string.IsNullOrWhiteSpace(request.ReasoningEffort)
                ? "low"
                : request.ReasoningEffort.Trim(),
            MaxTokens = Math.Clamp(request.MaxOutputTokens, 128, 512),
            Stream = false,
            Messages = new List<DeepSeekChatMessage>
            {
                new("system", BuildSystemPrompt()),
                new("user", BuildUserPrompt(request)),
            },
        };

        try
        {
            string raw = await client.CompleteChatAsync(apiKey, apiRequest, cancellationToken)
                .ConfigureAwait(false);
            return TryParse(raw, request.GiftCandidates, out AiGiftToolDecision? decision, out string failure)
                ? decision!
                : NoTool(failure);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return NoTool(Clean(ex.Message, 240, "Tool 决策失败"));
        }
    }

    public SocialGiftSelectionResult ValidateCall(
        GiftPolicyContext context,
        string? toolName,
        string? giftCandidateId)
    {
        string normalizedTool = (toolName ?? string.Empty).Trim();
        if (normalizedTool.Length == 0
            || normalizedTool.Equals(NpcGiftToolNames.None, StringComparison.Ordinal))
        {
            return giftPolicy.ValidateAiSelection(context, GiftPolicyService.TalkOnlyKey);
        }
        if (!normalizedTool.Equals(NpcGiftToolNames.GiveGift, StringComparison.Ordinal)
            && !normalizedTool.Equals(NpcGiftToolNames.MailGift, StringComparison.Ordinal))
        {
            return new SocialGiftSelectionResult
            {
                Kind = SocialGiftSelectionKind.Rejected,
                RejectionReason = SocialGiftRejectionReason.UnknownCandidateKey,
            };
        }

        return giftPolicy.ValidateAiSelection(context, giftCandidateId);
    }

    public static bool TryParse(
        string raw,
        IReadOnlyList<SocialSceneGiftOption> candidates,
        out AiGiftToolDecision? decision,
        out string failure)
    {
        decision = null;
        failure = string.Empty;
        GiftToolWireResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GiftToolWireResponse>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            failure = "give_gift Tool 返回的不是有效 JSON：" + Clean(ex.Message, 180, "解析失败");
            return false;
        }

        string toolName = (parsed?.Tool ?? string.Empty).Trim();
        string? candidateId = string.IsNullOrWhiteSpace(parsed?.GiftCandidateId)
            ? null
            : parsed!.GiftCandidateId!.Trim();
        if (toolName.Equals(NpcGiftToolNames.None, StringComparison.Ordinal))
        {
            if (candidateId is not null)
            {
                failure = "none Tool 不得携带礼物候选。";
                return false;
            }
        }
        else if (toolName.Equals(NpcGiftToolNames.GiveGift, StringComparison.Ordinal))
        {
            if (candidateId is null
                || !candidates.Any(candidate => candidate.CandidateId.Equals(
                    candidateId,
                    StringComparison.Ordinal)))
            {
                failure = $"{toolName} Tool 选择了未授权候选。";
                return false;
            }
        }
        else
        {
            failure = "Tool 名称无效。";
            return false;
        }

        decision = new AiGiftToolDecision
        {
            ToolName = toolName,
            GiftCandidateId = candidateId,
            ReasonTag = NormalizeTag(parsed?.ReasonTag),
        };
        return true;
    }

    private static string BuildSystemPrompt()
        => "你是 NPC 在本轮开口前使用的游戏行动规划器。根据 NPC 性格、存档事实、近期记忆、玩家本轮话语和活动，"
           + "决定是否送一份有上下文意义的礼物；不要为了提高频率而每轮都送，也不要因为玩家索要就自动同意。"
           + "只有当 NPC 会在当前见面时亲手交付，才选择 give_gift；否则选择 none。邮箱惊喜由每天结束后的独立系统决定，本轮禁止承诺邮寄、送到家或以后再送。"
           + "只能从用户提供的候选 id 中选择，禁止输出物品 ID。"
           + "只输出一个 JSON 对象，字段严格为 tool、giftCandidateId、reasonTag。tool 只能是 give_gift 或 none；none 时 giftCandidateId 必须为 null。";

    private static string BuildUserPrompt(AiGiftToolRequest request)
    {
        string candidateJson = JsonSerializer.Serialize(request.GiftCandidates.Select(candidate => new
        {
            id = candidate.CandidateId,
            name = candidate.DisplayName,
            reasonTags = candidate.ReasonTags,
            hint = candidate.Hint,
        }));
        return $"NPC：{Clean(request.NpcDisplayName, 80, request.NpcName)}\n"
               + $"当前存档与剧情：{Clean(request.GameContext, 6000, "无")}\n"
               + $"玩家本轮：{Clean(request.PlayerMessage, 600, "无")}\n"
               + $"近期对话：{Clean(request.RecentConversation, 900, "无")}\n"
               + $"近期活动：{Clean(request.ActivitySummary, 700, "无")}\n"
               + $"允许候选：{candidateJson}\n"
               + "先决定本轮行动，再按约定返回 JSON。";
    }

    public static string GuardVisibleReply(
        string reply,
        ConversationGiftExecutionResult execution,
        string npcDisplayName,
        out bool replaced)
    {
        ArgumentNullException.ThrowIfNull(execution);
        string normalized = (reply ?? string.Empty).Trim();
        bool invalid = execution.Outcome switch
        {
            ConversationGiftOutcome.ImmediateDelivered => ContainsMailClaim(normalized)
                                                          || !ContainsResolvedGiftName(normalized, execution.Candidate),
            ConversationGiftOutcome.MailScheduled => ContainsImmediateGiftClaim(normalized)
                                                       && !ContainsMailClaim(normalized)
                                                       || !ContainsResolvedGiftName(normalized, execution.Candidate),
            _ => ContainsImmediateGiftClaim(normalized) || ContainsMailClaim(normalized),
        };
        replaced = invalid || normalized.Length == 0;
        return replaced
            ? CreateFallbackReply(execution, npcDisplayName)
            : normalized;
    }

    /// <summary>
    /// Binds a proactive gift offer to the exact game-resolved item. A model line
    /// which omits that item name can otherwise describe a different gift while
    /// the choice button and actual delivery use the allowlisted candidate.
    /// </summary>
    public static string GuardGiftOfferDialogue(
        string dialogue,
        SocialGiftCandidate gift,
        out bool replaced)
    {
        ArgumentNullException.ThrowIfNull(gift);
        string normalized = (dialogue ?? string.Empty).Trim();
        replaced = normalized.Length == 0
                   || ContainsMailClaim(normalized)
                   || !ContainsResolvedGiftName(normalized, gift);
        if (!replaced)
            return normalized;

        string label = FormatGift(gift.DisplayName, gift.Quantity);
        return $"我觉得{label}现在会适合你。这个给你，愿意收下吗？";
    }

    public static string CreateFallbackReply(
        ConversationGiftExecutionResult execution,
        string npcDisplayName)
    {
        string npc = Clean(npcDisplayName, 80, "村民");
        if (execution.Candidate is not null && execution.IsImmediate)
        {
            string gift = FormatGift(execution.Candidate.DisplayName, execution.Quantity);
            return $"这个给你，拿好。希望它能派上用场。\n\n（{npc}当面递给你{gift}。）";
        }

        if (execution.Candidate is not null && execution.IsMail)
        {
            string gift = FormatGift(execution.Candidate.DisplayName, execution.Quantity);
            return $"我已经把{gift}安排寄出了。明天记得看看农舍外的邮箱。\n\n（{npc}确认礼物会在明天送达。）";
        }

        return "能和你聊这些，我很开心。你刚才说的话，我会认真记住的。";
    }

    private static bool ContainsImmediateGiftClaim(string value)
    {
        string lower = value.ToLowerInvariant();
        string[] signals =
        {
            "送给你", "送你", "这个给你", "这是给你的", "拿着", "收下", "递给你", "交给你", "给你带了",
            "给你准备了", "替你准备了", "礼物会", "已经送到", "家门口", "放在你家", "赠给你",
            "give you", "take this", "brought you", "a present for you", "a gift for you",
            "prepared a gift", "left it at your", "delivered it", "at your door",
        };
        return signals.Any(lower.Contains);
    }

    private static bool ContainsMailClaim(string value)
    {
        string lower = value.ToLowerInvariant();
        string[] signals =
        {
            "寄给你", "寄到", "邮寄", "邮箱", "信箱", "送到你家", "带到你家", "明天送", "明天寄",
            "农场门口", "明天会收到", "带回去给你", "送到农场",
            "mail it", "mailbox", "send it to your", "deliver it to your", "tomorrow's mail",
            "tomorrow you will receive", "send it home",
        };
        return signals.Any(lower.Contains);
    }

    private static bool ContainsResolvedGiftName(string value, SocialGiftCandidate? gift)
    {
        if (gift is null || string.IsNullOrWhiteSpace(gift.DisplayName))
            return false;

        string haystack = NormalizeForNameMatch(value);
        string needle = NormalizeForNameMatch(gift.DisplayName);
        return needle.Length > 0 && haystack.Contains(needle, StringComparison.Ordinal);
    }

    private static string NormalizeForNameMatch(string value)
        => new((value ?? string.Empty)
            .ToLowerInvariant()
            .Where(character => char.IsLetterOrDigit(character))
            .ToArray());

    private static string FormatGift(string displayName, int quantity)
    {
        string name = Clean(displayName, 120, "礼物");
        return quantity > 1 ? $"{name} ×{quantity}" : name;
    }

    private static AiGiftToolDecision NoTool(string failure = "")
        => new()
        {
            ToolName = NpcGiftToolNames.None,
            GiftCandidateId = null,
            UsedFallback = failure.Length > 0,
            FailureReason = failure,
        };

    private static string NormalizeTag(string? value)
    {
        string tag = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (tag.Length > 64)
            tag = tag[..64];
        return tag.All(character => character is >= 'a' and <= 'z'
                                    or >= '0' and <= '9'
                                    or '_')
            ? tag
            : string.Empty;
    }

    private static string Clean(string? value, int maximumCharacters, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Trim();
        if (clean.Length == 0)
            return fallback;
        return clean.Length <= maximumCharacters ? clean : clean[..maximumCharacters] + "…";
    }

    private sealed class GiftToolWireResponse
    {
        [JsonPropertyName("tool")]
        public string? Tool { get; set; }

        [JsonPropertyName("giftCandidateId")]
        public string? GiftCandidateId { get; set; }

        [JsonPropertyName("reasonTag")]
        public string? ReasonTag { get; set; }
    }
}
