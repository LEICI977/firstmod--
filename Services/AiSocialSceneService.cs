using System.Text;
using System.Text.Json;

namespace VivantValley.Services;

/// <summary>Generates one late-bound proactive line and an optional safe gift selection.</summary>
public sealed class AiSocialSceneService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly IDeepSeekClient client;

    public AiSocialSceneService(IDeepSeekClient client)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<AiSocialSceneDecision> GenerateAsync(
        string apiKey,
        AiSocialSceneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(apiKey))
            return Fallback(request, "API Key 未设置");

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
            MaxTokens = Math.Clamp(request.MaxOutputTokens, 128, 1024),
            Stream = false,
            Messages = new List<DeepSeekChatMessage>
            {
                new("system", BuildSystemPrompt(request)),
                new("user", BuildUserPrompt(request)),
            },
        };

        try
        {
            string raw = await client.CompleteChatAsync(apiKey, apiRequest, cancellationToken)
                .ConfigureAwait(false);
            return TryParse(raw, request, out AiSocialSceneDecision? decision, out string failure)
                ? decision!
                : Fallback(request, failure);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Fallback(request, CleanFailure(ex.Message));
        }
    }

    public static bool TryParse(
        string raw,
        AiSocialSceneRequest request,
        out AiSocialSceneDecision? decision,
        out string failure)
    {
        decision = null;
        failure = string.Empty;
        AiSocialSceneWireResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AiSocialSceneWireResponse>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            failure = "AI 主动场景不是有效 JSON：" + CleanFailure(ex.Message);
            return false;
        }

        if (parsed is null)
        {
            failure = "AI 主动场景返回了空 JSON。";
            return false;
        }

        int maxCharacters = Math.Clamp(request.MaxDialogueCharacters, 80, 1200);
        string dialogue = NormalizeDialogue(parsed.Dialogue, maxCharacters);
        if (dialogue.Length == 0)
        {
            failure = "AI 主动场景缺少 dialogue。";
            return false;
        }

        string action = (parsed.Action ?? string.Empty).Trim();
        string? giftCandidateId = string.IsNullOrWhiteSpace(parsed.GiftCandidateId)
            ? null
            : parsed.GiftCandidateId.Trim();
        if (action.Equals(SocialSceneActions.TalkOnly, StringComparison.Ordinal))
        {
            if (giftCandidateId is not null)
            {
                failure = "talk_only 不得携带礼物候选。";
                return false;
            }
        }
        else if (action.Equals(SocialSceneActions.Gift, StringComparison.Ordinal))
        {
            if (giftCandidateId is null
                || !request.GiftCandidates.Any(candidate => candidate.CandidateId.Equals(
                    giftCandidateId,
                    StringComparison.Ordinal)))
            {
                failure = "AI 选择了未授权的礼物候选。";
                return false;
            }
        }
        else
        {
            failure = "AI 主动场景 action 无效。";
            return false;
        }

        decision = new AiSocialSceneDecision
        {
            Dialogue = dialogue,
            Action = action,
            GiftCandidateId = giftCandidateId,
            MotiveTag = NormalizeTag(parsed.MotiveTag),
        };
        return true;
    }

    private static string BuildSystemPrompt(AiSocialSceneRequest request)
    {
        var builder = new StringBuilder(request.GameContext.Length + 1800);
        builder.AppendLine(request.GameContext.Trim());
        builder.AppendLine("【这次主动相遇的额外规则】");
        builder.AppendLine($"你今天恰好在正常行程中遇见玩家，并决定以 {request.NpcDisplayName} 的身份主动说一句话。不要追赶、传送或声称改变任何游戏数值。");
        builder.AppendLine("这不是固定剧情或恋爱脚本。根据近期真实聊天、玩家近期活动、当前存档事实与角色性格，做一次自然、克制、可独立成立的现实社交互动。");
        builder.AppendLine("不要增加原版好感，不要泄露未发生事件，不要虚构已经发生的共同经历。dialogue 必须是角色第一人称说的话，不写旁白、动作括号、角色名前缀或 Markdown。");
        builder.AppendLine("action 只能是 talk_only 或 gift。gift 表示请求调用 give_gift Tool。礼物不是必需；只有确实贴合上下文时才能选 gift，而且 giftCandidateId 只能从用户消息给出的候选 ID 中精确选择。不能输出物品 ID。");
        builder.AppendLine("若选择 gift，dialogue 要像当面交流一样自然地提出这份礼物；若选择 talk_only，不要声称已经送出任何东西。");
        builder.AppendLine("只输出一个 JSON 对象，字段严格为 dialogue、action、giftCandidateId、motiveTag；talk_only 时 giftCandidateId 必须为 null。不要代码围栏或额外文字。");
        return builder.ToString().Trim();
    }

    private static string BuildUserPrompt(AiSocialSceneRequest request)
    {
        var builder = new StringBuilder(1800);
        builder.AppendLine("近期对话摘录：");
        builder.AppendLine(CleanContext(request.RecentConversation, 1200, "无"));
        builder.AppendLine("对话信号：");
        builder.AppendLine(CleanContext(request.SignalSummary, 600, "无"));
        builder.AppendLine("玩家最近七日活动摘要：");
        builder.AppendLine(CleanContext(request.ActivitySummary, 800, "无"));
        builder.AppendLine("代码允许选择的礼物候选：");
        if (request.GiftCandidates.Count == 0)
        {
            builder.AppendLine("[]（本次只能 talk_only）");
        }
        else
        {
            string json = JsonSerializer.Serialize(request.GiftCandidates.Select(candidate => new
            {
                id = candidate.CandidateId,
                name = candidate.DisplayName,
                reasonTags = candidate.ReasonTags,
                hint = candidate.Hint,
            }));
            builder.AppendLine(json);
        }

        builder.AppendLine("按约定返回 JSON。dialogue 通常 1 到 3 句，像路上真实碰见时会说的话。motiveTag 用一个简短 snake_case 标签概括动机。");
        return builder.ToString().Trim();
    }

    private static AiSocialSceneDecision Fallback(AiSocialSceneRequest request, string failure)
    {
        string fallback = NormalizeDialogue(request.FallbackDialogue, request.MaxDialogueCharacters);
        if (fallback.Length == 0)
            fallback = "上次聊过以后，我还记着你说的那些。今天正好碰见，就想问问你最近还好吗？";

        return new AiSocialSceneDecision
        {
            Dialogue = fallback,
            Action = SocialSceneActions.TalkOnly,
            GiftCandidateId = null,
            MotiveTag = "fallback_check_in",
            UsedFallback = true,
            FailureReason = CleanFailure(failure),
        };
    }

    private static string NormalizeDialogue(string? value, int maximumCharacters)
    {
        string normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        maximumCharacters = Math.Clamp(maximumCharacters, 80, 1200);
        return normalized.Length <= maximumCharacters
            ? normalized
            : normalized[..maximumCharacters] + "…";
    }

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

    private static string CleanContext(string? value, int maximumCharacters, string fallback)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Trim();
        if (clean.Length == 0)
            return fallback;
        return clean.Length <= maximumCharacters ? clean : clean[..maximumCharacters] + "…";
    }

    private static string CleanFailure(string? value)
    {
        string clean = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= 240 ? clean : clean[..240] + "…";
    }
}
