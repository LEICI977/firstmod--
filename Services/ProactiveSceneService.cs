namespace VivantValley.Services;

/// <summary>Generates dialogue flavor for a deterministic proactive encounter.</summary>
public sealed class ProactiveSceneService
{
    private readonly IDeepSeekClient deepSeekClient;

    public ProactiveSceneService(IDeepSeekClient deepSeekClient)
    {
        this.deepSeekClient = deepSeekClient ?? throw new ArgumentNullException(nameof(deepSeekClient));
    }

    public Task<string> GenerateAsync(
        string apiKey,
        string npcSystemPrompt,
        NpcConversationMemory memory,
        PlannedNpcEncounter encounter,
        string giftDisplayName,
        ConversationEngineOptions options,
        CancellationToken cancellationToken)
    {
        string storyBrief = string.IsNullOrWhiteSpace(encounter.AiBrief)
            ? "你记得玩家最近说过的话，今天主动走近玩家，关心对方，并准备当面送出一份小礼物。"
            : encounter.AiBrief.Trim();
        string sceneContract = string.IsNullOrWhiteSpace(encounter.GiftItemId)
            ? "\n你在同一地点主动走近玩家。这次不交付物品，重点是把剧情中的问题或邀请自然地说出来。"
            : "\n你在同一地点主动走近玩家，准备当面送出一份小礼物：" + giftDisplayName + "。";
        var messages = new List<DeepSeekChatMessage>
        {
            new(
                "system",
                npcSystemPrompt.Trim()
                + "\n【本次确定发生的剧情节点】"
                + storyBrief
                + sceneContract
                + "这件事已经由游戏规则决定，不要改变地点、物品或结果。只输出你对玩家说的自然台词。"
                + "不要写角色名前缀、舞台说明、Markdown 或选项；不要声称修改了好感或游戏数值；控制在 180 个汉字以内。"),
        };

        if (!string.IsNullOrWhiteSpace(memory.Summary))
            messages.Add(new DeepSeekChatMessage("system", "较早的共同记忆：\n" + memory.Summary.Trim()));

        int first = Math.Max(0, memory.Messages.Count - Math.Min(8, options.MaxContextMessages));
        for (int index = first; index < memory.Messages.Count; index++)
        {
            ConversationMemoryMessage message = memory.Messages[index];
            if (message is null || string.IsNullOrWhiteSpace(message.Content))
                continue;

            string role = message.Role?.Trim().ToLowerInvariant() == "assistant" ? "assistant" : "user";
            messages.Add(new DeepSeekChatMessage(role, message.Content.Trim()));
        }

        messages.Add(new DeepSeekChatMessage(
            "user",
            "玩家最近一次提到的内容是：\n"
            + (string.IsNullOrWhiteSpace(encounter.TriggerExcerpt) ? "（没有可引用的原文，请自然地表达关心。）" : encounter.TriggerExcerpt)
            + "\n现在直接说出本次主动来访的台词。"));

        var request = new DeepSeekChatRequest
        {
            Model = options.Model,
            Messages = messages,
            Thinking = new DeepSeekThinkingOptions { Type = options.ThinkingType },
            ReasoningEffort = options.ReasoningEffort,
            MaxTokens = Math.Clamp(options.MaxOutputTokens, 128, 512),
            Stream = false,
        };

        return deepSeekClient.CompleteChatAsync(apiKey, request, cancellationToken);
    }
}
