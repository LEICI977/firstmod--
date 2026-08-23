using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VivantValley;

/// <summary>
/// Builds chat context from a single player/NPC memory snapshot and performs
/// loss-minimizing memory compaction before requesting the NPC reply.
/// </summary>
public sealed class ConversationEngine
{
    private const int MaximumSummaryCharacters = 6000;

    private const string SummarySystemPrompt =
        "你是长期对话记忆整理器。请把提供的旧摘要和旧消息压缩成一份准确、简洁、可供后续角色扮演使用的中文记忆。"
        + "保留人物关系、承诺、偏好、重要事实、情绪变化、未完成事项和游戏剧情相关信息；不要编造事实；不要回答玩家；"
        + "只输出更新后的摘要，并尽量控制在 1500 个汉字以内。";

    private readonly IDeepSeekClient deepSeekClient;

    public ConversationEngine(IDeepSeekClient deepSeekClient)
    {
        this.deepSeekClient = deepSeekClient ?? throw new ArgumentNullException(nameof(deepSeekClient));
    }

    /// <summary>
    /// Generates one reply. The supplied memory snapshot is cloned and never
    /// changed in-place, so callers can persist the returned snapshot only after
    /// the entire operation succeeds.
    /// </summary>
    public Task<ConversationEngineResult> GenerateReplyAsync(
        string apiKey,
        string systemPrompt,
        NpcConversationMemory memorySnapshot,
        string userText,
        string? currentGameDate,
        ConversationEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return GenerateReplyCoreAsync(
            apiKey,
            systemPrompt,
            memorySnapshot,
            userText,
            currentGameDate,
            options,
            onChunk: null,
            cancellationToken);
    }

    public Task<ConversationEngineResult> GenerateReplyStreamingAsync(
        string apiKey,
        string systemPrompt,
        NpcConversationMemory memorySnapshot,
        string userText,
        string? currentGameDate,
        Action<DeepSeekStreamChunk> onChunk,
        ConversationEngineOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (onChunk is null)
            throw new ArgumentNullException(nameof(onChunk));

        return GenerateReplyCoreAsync(
            apiKey,
            systemPrompt,
            memorySnapshot,
            userText,
            currentGameDate,
            options,
            onChunk,
            cancellationToken);
    }

    private async Task<ConversationEngineResult> GenerateReplyCoreAsync(
        string apiKey,
        string systemPrompt,
        NpcConversationMemory memorySnapshot,
        string userText,
        string? currentGameDate,
        ConversationEngineOptions? options,
        Action<DeepSeekStreamChunk>? onChunk,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new ArgumentException("系统提示不能为空。", nameof(systemPrompt));
        if (memorySnapshot is null)
            throw new ArgumentNullException(nameof(memorySnapshot));
        if (string.IsNullOrWhiteSpace(userText))
            throw new ArgumentException("玩家文本不能为空。", nameof(userText));

        options ??= new ConversationEngineOptions();
        ValidateOptions(options);

        NpcConversationMemory workingMemory = memorySnapshot.Clone();
        workingMemory.Messages ??= new List<ConversationMemoryMessage>();
        workingMemory.Summary = LimitSummary(workingMemory.Summary);

        var compaction = new MemoryCompactionInfo
        {
            PreviousSummary = workingMemory.Summary,
            UpdatedSummary = workingMemory.Summary,
            KeptMessageCount = workingMemory.Messages.Count,
        };

        int projectedMessageCount = checked(workingMemory.Messages.Count + 2);
        compaction.ThresholdExceeded = projectedMessageCount > options.SummaryTriggerMessageCount;

        int candidateOldMessageCount = Math.Max(
            0,
            workingMemory.Messages.Count - options.RecentMessagesToKeep);
        int summaryBatchCount = Math.Min(candidateOldMessageCount, options.MaxContextMessages);
        if (compaction.ThresholdExceeded && summaryBatchCount > 0)
        {
            compaction.SummaryAttempted = true;
            List<ConversationMemoryMessage> oldMessages = workingMemory.Messages.GetRange(0, summaryBatchCount);
            List<ConversationMemoryMessage> remainingMessages = workingMemory.Messages.GetRange(
                summaryBatchCount,
                workingMemory.Messages.Count - summaryBatchCount);

            try
            {
                string updatedSummary = await SummarizeAsync(
                        apiKey,
                        workingMemory.Summary,
                        oldMessages,
                        options,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(updatedSummary))
                    throw new DeepSeekApiException("记忆总结请求返回了空摘要。");

                workingMemory.Summary = LimitSummary(updatedSummary);
                workingMemory.Messages = remainingMessages;
                compaction.SummarySucceeded = true;
                compaction.PrunedMessageCount = summaryBatchCount;
                compaction.KeptMessageCount = remainingMessages.Count;
                compaction.UpdatedSummary = workingMemory.Summary;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Summary is an optimization. Preserve the full original history and
                // continue with the actual chat when that optimization is unavailable.
                compaction.ContinuedAfterSummaryFailure = true;
                compaction.SummarySucceeded = false;
                compaction.PrunedMessageCount = 0;
                compaction.KeptMessageCount = workingMemory.Messages.Count;
                compaction.SummaryFailureReason = SanitizeDiagnostic(exception.Message, apiKey);
            }
        }

        var chatMessages = new List<DeepSeekChatMessage>
        {
            new("system", systemPrompt.Trim()),
        };

        if (!string.IsNullOrWhiteSpace(workingMemory.Summary))
        {
            chatMessages.Add(new DeepSeekChatMessage(
                "system",
                "以下是该玩家与该村民较早对话的长期记忆摘要。把它视为背景记忆，不要逐字复述：\n"
                + workingMemory.Summary.Trim()));
        }

        int firstContextMessage = Math.Max(0, workingMemory.Messages.Count - options.MaxContextMessages);
        for (int index = firstContextMessage; index < workingMemory.Messages.Count; index++)
        {
            ConversationMemoryMessage message = workingMemory.Messages[index];
            if (message is null || string.IsNullOrWhiteSpace(message.Content))
                continue;

            string content = message.Content.Trim();
            content = AddSourceLabel(content, message.Source);
            if (!string.IsNullOrWhiteSpace(message.GameDate))
                content = $"[{message.GameDate.Trim()}] {content}";

            chatMessages.Add(new DeepSeekChatMessage(
                NormalizeRole(message.Role),
                content));
        }

        string normalizedUserText = userText.Trim();
        chatMessages.Add(new DeepSeekChatMessage("user", normalizedUserText));

        DeepSeekChatRequest chatRequest = CreateRequest(
            chatMessages,
            options,
            stream: onChunk is not null);
        string reply;
        if (onChunk is null)
        {
            reply = await deepSeekClient.CompleteChatAsync(
                    apiKey,
                    chatRequest,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            reply = await deepSeekClient.StreamChatAsync(
                    apiKey,
                    chatRequest,
                    onChunk,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        string date = string.IsNullOrWhiteSpace(currentGameDate)
            ? workingMemory.LastDate ?? string.Empty
            : currentGameDate.Trim();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        workingMemory.Messages.Add(new ConversationMemoryMessage
        {
            Role = "user",
            Content = normalizedUserText,
            GameDate = date,
            CreatedAtUtc = now,
            Source = ConversationMemorySources.AiChat,
        });
        workingMemory.Messages.Add(new ConversationMemoryMessage
        {
            Role = "assistant",
            Content = reply,
            GameDate = date,
            CreatedAtUtc = now,
            Source = ConversationMemorySources.AiChat,
        });
        workingMemory.TotalTurns = checked(workingMemory.TotalTurns + 1);
        workingMemory.LastDate = date;

        return new ConversationEngineResult
        {
            Reply = reply,
            UpdatedMemory = workingMemory,
            Compaction = compaction,
        };
    }

    private async Task<string> SummarizeAsync(
        string apiKey,
        string previousSummary,
        IReadOnlyList<ConversationMemoryMessage> oldMessages,
        ConversationEngineOptions options,
        CancellationToken cancellationToken)
    {
        var input = new StringBuilder();
        input.AppendLine("【已有摘要】");
        input.AppendLine(string.IsNullOrWhiteSpace(previousSummary) ? "（无）" : previousSummary.Trim());
        input.AppendLine();
        input.AppendLine("【需要并入摘要的旧消息，按时间顺序】");

        foreach (ConversationMemoryMessage message in oldMessages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Content))
                continue;

            if (!string.IsNullOrWhiteSpace(message.GameDate))
                input.Append('[').Append(message.GameDate.Trim()).Append("] ");

            string speaker = NormalizeRole(message.Role) switch
            {
                "assistant" => "村民",
                "system" => "游戏记录",
                _ => "玩家",
            };
            input.Append(speaker)
                .Append(": ")
                .AppendLine(AddSourceLabel(message.Content.Trim(), message.Source));
        }

        var messages = new List<DeepSeekChatMessage>
        {
            new("system", SummarySystemPrompt),
            new("user", input.ToString()),
        };

        return await deepSeekClient.CompleteChatAsync(
                apiKey,
                CreateRequest(messages, options),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static DeepSeekChatRequest CreateRequest(
        List<DeepSeekChatMessage> messages,
        ConversationEngineOptions options,
        bool stream = false)
    {
        return new DeepSeekChatRequest
        {
            Model = options.Model.Trim(),
            Messages = messages,
            Thinking = new DeepSeekThinkingOptions
            {
                Type = options.ThinkingType.Trim(),
            },
            ReasoningEffort = options.ReasoningEffort.Trim(),
            MaxTokens = options.MaxOutputTokens,
            Stream = stream,
        };
    }

    private static void ValidateOptions(ConversationEngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            throw new ArgumentException("Model 不能为空。", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ThinkingType))
            throw new ArgumentException("ThinkingType 不能为空。", nameof(options));
        if (string.IsNullOrWhiteSpace(options.ReasoningEffort))
            throw new ArgumentException("ReasoningEffort 不能为空。", nameof(options));
        if (options.MaxContextMessages < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxContextMessages 至少为 1。");
        if (options.MaxOutputTokens < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxOutputTokens 至少为 1。");
        if (options.SummaryTriggerMessageCount < 2)
            throw new ArgumentOutOfRangeException(nameof(options), "SummaryTriggerMessageCount 至少为 2。");
        if (options.RecentMessagesToKeep < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "RecentMessagesToKeep 不能小于 0。");
        if (options.RecentMessagesToKeep >= options.SummaryTriggerMessageCount)
        {
            throw new ArgumentException(
                "RecentMessagesToKeep 必须小于 SummaryTriggerMessageCount。",
                nameof(options));
        }
    }

    private static string NormalizeRole(string? role)
    {
        return role?.Trim().ToLowerInvariant() switch
        {
            "assistant" => "assistant",
            "system" => "system",
            // Historic releases stored tool facts without an OpenAI tool-call ID.
            // Replay them as system facts instead of issuing an invalid tool message.
            "tool" => "system",
            _ => "user",
        };
    }

    private static string AddSourceLabel(string content, string? source)
        => source?.Trim() switch
        {
            ConversationMemorySources.VanillaDialogue => "[原版游戏对话] " + content,
            ConversationMemorySources.VanillaChoice => "[原版游戏选择] " + content,
            ConversationMemorySources.VanillaGift => "[原版游戏送礼] " + content,
            ConversationMemorySources.ModGift => "[Mod 已执行动作] " + content,
            ConversationMemorySources.ModAction => "[Mod 已执行动作] " + content,
            ConversationMemorySources.ModProactive => "[Mod 主动剧情] " + content,
            ConversationMemorySources.ModSocial => "[Mod 主动相遇] " + content,
            ConversationMemorySources.ModMail => "[Mod 邮件] " + content,
            _ => content,
        };

    private static string SanitizeDiagnostic(string? value, string apiKey)
    {
        string sanitized = value ?? string.Empty;
        if (!string.IsNullOrEmpty(apiKey))
            sanitized = sanitized.Replace(apiKey.Trim(), "[REDACTED]", StringComparison.Ordinal);

        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= 500 ? sanitized : sanitized.Substring(0, 500) + "…";
    }

    private static string LimitSummary(string? value)
    {
        string summary = (value ?? string.Empty).Trim();
        return summary.Length <= MaximumSummaryCharacters
            ? summary
            : summary[..MaximumSummaryCharacters] + "…";
    }
}
