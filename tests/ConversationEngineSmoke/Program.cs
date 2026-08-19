using VivantValley;
using VivantValley.Services;
using System.Net;
using System.Text;
using System.Text.Json;

TestAiBaseUrlResolution();
await TestAiProviderPayloadsAsync();
await TestOpenAiStreamingAdapterAsync();
await TestRecentContextLimitAsync();
await TestStreamingReplyAsync();
await TestBoundedCompactionBatchAsync();
await TestSummaryFailureFallbackAsync();
await TestSummaryLengthLimitAsync();
await TestVanillaSourceLabelAsync();
TestNarrativeStoreIsolation();
TestStoryCatalogLoading();
TestDataDrivenStoryScheduling();
TestAuthoredAbigailArc();
TestPilotNarrativeScheduling();
TestPilotNarrativeDeferral();
await TestProactiveScenePromptAsync();
await TestProactiveSceneWithoutGiftPromptAsync();
TestDailySocialPlannerDeterminism();
TestDailySocialPlannerEligibility();
await TestConversationSignalExtractionAsync();
await TestAiSocialSceneContractAsync();
await TestNpcGiftToolContractAsync();
await TestOvernightMailPlannerContractAsync();
TestGiftPolicySafety();
TestGiftPolicySchemaV2();
TestProductionGiftCatalog();
TestConversationContinuationPolicy();
TestMailGiftPersistenceModel();
TestGiftMailContent();
TestOvernightMailPersistenceModel();
TestVanillaMemoryMetadataClone();
TestNarrativeEpisodeContextLayering();
TestNarrativeEpisodeForget();
TestOldMemoryJsonCompatibility();
TestLangGraphDecisionValidation();
await TestLangGraphClientContractAsync();

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
    await TestLiveStreamingAsync();
if (args.Contains("--langgraph-live", StringComparer.OrdinalIgnoreCase))
    await TestLiveLangGraphTransportAsync();

Console.WriteLine("Conversation engine smoke tests passed.");

static async Task TestLiveLangGraphTransportAsync()
{
    using var httpClient = new HttpClient();
    var client = new LangGraphClient(httpClient, "http://127.0.0.1:8123", TimeSpan.FromSeconds(15));
    try
    {
        await client.DecideAsync(new LangGraphRequest
        {
            RequestId = "transport-regression",
            PlayerId = "1",
            NpcName = "Haley",
            Day = 1,
            Location = "Town",
            ActionId = "transport-regression",
            ContextVersion = "ctx-transport",
            Mode = "conversation",
            ContextSnapshot = new NpcContextSnapshot
            {
                NpcName = "Haley",
                PlayerInput = "hello",
                PlayerId = "1",
                Day = 1,
                Location = "Town",
                ActionId = "transport-regression",
                ContextVersion = "ctx-transport",
            },
            Llm = new LangGraphLlmProfile
            {
                Provider = AiProviderNames.DeepSeek,
                BaseUrl = "http://127.0.0.1:9",
                Model = "transport-test",
                ApiKey = "transport-test-key",
                MaxOutputTokens = 128,
            },
        });
        throw new InvalidOperationException("LangGraph transport test expected the provider to fail.");
    }
    catch (LangGraphException exception)
    {
        Assert(exception.StatusCode == HttpStatusCode.BadGateway, "Graph service did not return a controlled 502.");
        Console.WriteLine("Live LangGraph request upload passed; downstream failure returned controlled HTTP 502.");
    }
}

static void TestLangGraphDecisionValidation()
{
    var snapshot = new NpcContextSnapshot
    {
        NpcName = "Abigail",
        ContextVersion = "ctx-1",
        AllowedTools = new[]
        {
            new LangGraphGiftCandidate { CandidateKey = "abigail_quartz", DisplayName = "Quartz" },
        },
    };
    var validator = new DecisionValidator();
    LangGraphDecision decision = validator.Validate(
        new LangGraphResponse
        {
            RequestId = "req-1",
            ContextVersion = "ctx-1",
            Decision = new LangGraphDecision
            {
                Action = new LangGraphAction
                {
                    Name = NpcGiftToolNames.GiveGift,
                    CandidateKey = "abigail_quartz",
                    Delivery = SocialGiftDeliveryModes.Immediate,
                },
                Reply = "收下这份 Quartz 吧。",
                MemoryUpdate = new LangGraphMemoryUpdate
                {
                    Signal = new LangGraphSignal { Valence = 4, Warmth = -1 },
                },
            },
        },
        snapshot,
        1200,
        "req-1");
    Assert(decision.Action.CandidateKey == "abigail_quartz", "Graph candidate was not retained.");
    Assert(decision.MemoryUpdate.Signal.Valence == 1d, "Signal valence was not bounded.");
    Assert(decision.MemoryUpdate.Signal.Warmth == 0d, "Signal warmth was not bounded.");

    bool rejected = false;
    try
    {
        validator.Validate(
            new LangGraphResponse
            {
                RequestId = "req-1",
                ContextVersion = "ctx-1",
                Decision = new LangGraphDecision
                {
                    Action = new LangGraphAction
                    {
                        Name = NpcGiftToolNames.GiveGift,
                        CandidateKey = "forged-item-id",
                    },
                    Reply = "reply",
                },
            },
            snapshot,
            1200,
            "req-1");
    }
    catch (LangGraphValidationException)
    {
        rejected = true;
    }
    Assert(rejected, "Validator accepted a candidate outside the allowlist.");

    rejected = false;
    try
    {
        validator.Validate(
            new LangGraphResponse
            {
                RequestId = "req-1",
                ContextVersion = "ctx-1",
                Decision = new LangGraphDecision
                {
                    Reply = "{\"tool\":\"none\"}",
                },
            },
            snapshot,
            1200,
            "req-1");
    }
    catch (LangGraphValidationException)
    {
        rejected = true;
    }
    Assert(rejected, "Validator accepted a JSON reply.");
}

static async Task TestLangGraphClientContractAsync()
{
    using var httpClient = new HttpClient(new RecordingHttpHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"requestId\":\"req-1\",\"contextVersion\":\"ctx-1\",\"decision\":{\"schema_version\":1,\"decision\":\"reply\",\"action\":{\"name\":\"none\",\"candidate_key\":null,\"delivery\":\"immediate\",\"reason_tag\":\"\"},\"reply\":\"hello\",\"memory_update\":{\"summary_patch\":\"\",\"signal\":{},\"topics\":[],\"open_loops\":[]}}}",
            Encoding.UTF8,
            "application/json"),
    }));
    var client = new LangGraphClient(httpClient, "http://127.0.0.1:8123", TimeSpan.FromSeconds(10));
    LangGraphResponse response = await client.DecideAsync(new LangGraphRequest
    {
        RequestId = "req-1",
        ContextVersion = "ctx-1",
        ContextSnapshot = new NpcContextSnapshot { ContextVersion = "ctx-1" },
    });
    Assert(response.Decision.Reply == "hello", "LangGraph client failed to deserialize the response.");
}

static void TestAiBaseUrlResolution()
{
    Assert(
        AiEndpointResolver.TryResolve(
            AiProviderNames.OpenAI,
            "https://api.openai.com/v1",
            out string openAiBase,
            out Uri openAiEndpoint,
            out _),
        "OpenAI base URL wasn't accepted.");
    Assert(openAiBase == "https://api.openai.com/v1", "OpenAI base URL wasn't normalized.");
    Assert(
        openAiEndpoint.AbsoluteUri == "https://api.openai.com/v1/chat/completions",
        "OpenAI chat-completions route wasn't appended.");

    Assert(
        AiEndpointResolver.TryResolve(
            AiProviderNames.OpenAI,
            "https://proxy.example/openai/v1/chat/completions/",
            out string proxyBase,
            out Uri proxyEndpoint,
            out _),
        "A full proxy endpoint wasn't accepted.");
    Assert(proxyBase == "https://proxy.example/openai/v1", "Full endpoint wasn't reduced to a base URL.");
    Assert(
        proxyEndpoint.AbsoluteUri == "https://proxy.example/openai/v1/chat/completions",
        "Proxy subpath was lost while rebuilding the endpoint.");

    Assert(
        AiEndpointResolver.TryResolve(
            AiProviderNames.DeepSeek,
            "https://api.deepseek.com/",
            out string deepSeekBase,
            out Uri deepSeekEndpoint,
            out _),
        "DeepSeek base URL wasn't accepted.");
    Assert(deepSeekBase == "https://api.deepseek.com", "DeepSeek base URL retained a trailing slash.");
    Assert(
        deepSeekEndpoint.AbsoluteUri == "https://api.deepseek.com/chat/completions",
        "DeepSeek chat-completions route wasn't appended.");

    Assert(
        !AiEndpointResolver.TryResolve(
            AiProviderNames.OpenAI,
            "http://remote.example/v1",
            out _,
            out _,
            out _),
        "An insecure remote HTTP endpoint was accepted.");
    Assert(
        AiEndpointResolver.TryResolve(
            AiProviderNames.OpenAI,
            "http://127.0.0.1:11434/v1",
            out _,
            out _,
            out _),
        "A loopback HTTP endpoint was rejected.");
}

static async Task TestAiProviderPayloadsAsync()
{
    var handler = new RecordingHttpHandler(() => JsonResponse("provider-ok"));
    using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    AiRuntimeProfile profile = CreateAiProfile(
        AiProviderNames.DeepSeek,
        "https://api.deepseek.com",
        "deepseek-test",
        "deep-key");
    var client = new AiProviderClient(httpClient, () => profile);
    var request = new DeepSeekChatRequest
    {
        Model = "ignored-request-model",
        Messages = new List<DeepSeekChatMessage> { new("user", "hello") },
        Thinking = new DeepSeekThinkingOptions { Type = "disabled" },
        ReasoningEffort = "low",
        MaxTokens = 321,
        Stream = false,
    };

    string deepSeekReply = await client.CompleteChatAsync("ignored-key", request);
    Assert(deepSeekReply == "provider-ok", "DeepSeek adapter didn't parse the reply.");
    CapturedHttpRequest deepSeekCall = handler.Requests.Single();
    using (JsonDocument body = JsonDocument.Parse(deepSeekCall.Body))
    {
        JsonElement root = body.RootElement;
        Assert(root.GetProperty("model").GetString() == "deepseek-test", "Runtime DeepSeek model wasn't used.");
        Assert(root.GetProperty("max_tokens").GetInt32() == 321, "DeepSeek max_tokens wasn't emitted.");
        Assert(root.TryGetProperty("thinking", out _), "DeepSeek thinking settings weren't emitted.");
        Assert(root.TryGetProperty("reasoning_effort", out _), "DeepSeek reasoning effort wasn't emitted.");
        Assert(!root.TryGetProperty("max_completion_tokens", out _), "OpenAI token field leaked into DeepSeek.");
    }
    Assert(deepSeekCall.Authorization == "Bearer deep-key", "DeepSeek authorization used the wrong key.");

    handler.Requests.Clear();
    profile = CreateAiProfile(
        AiProviderNames.OpenAI,
        "https://api.openai.com/v1",
        "gpt-user-model",
        "openai-key");
    string openAiReply = await client.CompleteChatAsync("ignored-key", request);
    Assert(openAiReply == "provider-ok", "OpenAI adapter didn't parse the reply.");
    CapturedHttpRequest openAiCall = handler.Requests.Single();
    Assert(
        openAiCall.Uri.AbsoluteUri == "https://api.openai.com/v1/chat/completions",
        "OpenAI adapter used the wrong endpoint.");
    Assert(openAiCall.Authorization == "Bearer openai-key", "OpenAI authorization used the wrong key.");
    using (JsonDocument body = JsonDocument.Parse(openAiCall.Body))
    {
        JsonElement root = body.RootElement;
        Assert(root.GetProperty("model").GetString() == "gpt-user-model", "Runtime OpenAI model wasn't used.");
        Assert(
            root.GetProperty("max_completion_tokens").GetInt32() == 321,
            "OpenAI max_completion_tokens wasn't emitted.");
        Assert(!root.TryGetProperty("thinking", out _), "DeepSeek thinking leaked into OpenAI.");
        Assert(!root.TryGetProperty("reasoning_effort", out _), "DeepSeek reasoning effort leaked into OpenAI.");
        Assert(!root.TryGetProperty("max_tokens", out _), "DeepSeek max_tokens leaked into OpenAI.");
    }
}

static async Task TestOpenAiStreamingAdapterAsync()
{
    const string stream = "data: {\"choices\":[{\"delta\":{\"content\":\"STREAM_\"}}]}\n\n"
                          + "data: {\"choices\":[{\"delta\":{\"content\":\"OK\"}}]}\n\n"
                          + "data: [DONE]\n\n";
    var handler = new RecordingHttpHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(stream, Encoding.UTF8, "text/event-stream"),
    });
    using var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    AiRuntimeProfile profile = CreateAiProfile(
        AiProviderNames.OpenAI,
        "https://api.openai.com/v1",
        "gpt-user-model",
        "openai-key");
    var client = new AiProviderClient(httpClient, () => profile);
    var chunks = new List<DeepSeekStreamChunk>();
    string reply = await client.StreamChatAsync(
        "ignored-key",
        new DeepSeekChatRequest
        {
            Model = "ignored",
            Messages = new List<DeepSeekChatMessage> { new("user", "stream") },
            MaxTokens = 64,
            Stream = true,
        },
        chunks.Add);

    Assert(reply == "STREAM_OK", "OpenAI stream chunks weren't assembled.");
    Assert(chunks.All(chunk => chunk.ReasoningDelta.Length == 0), "OpenAI stream exposed DeepSeek reasoning chunks.");
}

static AiRuntimeProfile CreateAiProfile(string provider, string baseUrl, string model, string key)
{
    Assert(
        AiEndpointResolver.TryResolve(provider, baseUrl, out string normalized, out Uri endpoint, out string failure),
        "Test AI profile is invalid: " + failure);
    return new AiRuntimeProfile(
        provider,
        normalized,
        endpoint,
        model,
        key,
        "test",
        TimeSpan.FromSeconds(10),
        EnableThinking: false,
        ReasoningEffort: "low");
}

static HttpResponseMessage JsonResponse(string reply)
    => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                choices = new[] { new { message = new { content = reply } } },
            }),
            Encoding.UTF8,
            "application/json"),
    };

static async Task TestVanillaSourceLabelAsync()
{
    var memory = new NpcConversationMemory();
    memory.Messages.Add(new ConversationMemoryMessage
    {
        Role = "assistant",
        Content = "今天天气真不错。",
        Source = ConversationMemorySources.VanillaDialogue,
        GameDate = "Y1 spring 1 900",
    });
    var client = new RecordingClient("reply");
    var engine = new ConversationEngine(client);
    await engine.GenerateReplyAsync(
        "test-key",
        "system",
        memory,
        "是啊",
        "Y1 spring 1 910",
        new ConversationEngineOptions
        {
            MaxContextMessages = 8,
            SummaryTriggerMessageCount = 20,
            RecentMessagesToKeep = 4,
        });

    Assert(
        client.Requests[0].Messages.Any(message => message.Content.Contains("[原版游戏对话] 今天天气真不错。")),
        "Vanilla dialogue source wasn't identified in the AI prompt.");
}

static void TestOldMemoryJsonCompatibility()
{
    const string json = "{\"Players\":{\"player\":{\"Abigail\":{\"PlayerId\":\"player\",\"NpcName\":\"Abigail\",\"Summary\":\"old\",\"Messages\":[{\"Role\":\"assistant\",\"Content\":\"legacy\",\"GameDate\":\"Y1 spring 1\"}],\"TotalTurns\":1,\"LastDate\":\"Y1 spring 1\"}}}}";
    ConversationMemoryStore? store = JsonSerializer.Deserialize<ConversationMemoryStore>(json);
    Assert(store is not null, "Old memory JSON couldn't be deserialized.");
    store!.Normalize();
    Assert(store.TryGet("player", "Abigail", out NpcConversationMemory? memory) && memory is not null, "Old NPC memory was lost.");
    Assert(memory!.Messages[0].Source == string.Empty, "Missing source metadata didn't receive a compatible default.");
    Assert(store.GetNarrativeEpisodes("player").Count == 0, "Old save unexpectedly created narrative episodes.");
}

static void TestVanillaMemoryMetadataClone()
{
    var original = new ConversationMemoryMessage
    {
        Role = "assistant",
        Content = "原版台词",
        GameDate = "Y1 spring 2 900",
        Source = ConversationMemorySources.VanillaDialogue,
        EventId = "901756",
        EpisodeId = "episode-1",
        LocationName = "Saloon",
        TranslationKey = "Characters/Dialogue/Abigail:Mon",
        DedupeKey = "dedupe-1",
    };

    ConversationMemoryMessage clone = original.Clone();
    Assert(clone.Source == ConversationMemorySources.VanillaDialogue, "Vanilla source wasn't cloned.");
    Assert(clone.EventId == "901756" && clone.EpisodeId == "episode-1", "Event metadata wasn't cloned.");
    Assert(clone.LocationName == "Saloon" && clone.DedupeKey == "dedupe-1", "Interaction metadata wasn't cloned.");
}

static void TestNarrativeEpisodeContextLayering()
{
    NarrativeEpisode oldEpisode = CreateEpisode("old", 5, completed: true);
    oldEpisode.Beats.Add(new NarrativeBeat
    {
        Sequence = 1,
        Kind = NarrativeBeatKinds.NpcDialogue,
        NpcName = "Abigail",
        SpeakerName = "Abigail",
        SpeakerDisplayName = "阿比盖尔",
        Text = "你真的愿意陪我去矿洞吗？",
    });
    oldEpisode.Beats.Add(new NarrativeBeat
    {
        Sequence = 2,
        Kind = NarrativeBeatKinds.PlayerChoice,
        NpcName = "Abigail",
        SpeakerName = "Farmer",
        Text = "我会陪你一起去。",
    });
    oldEpisode.Beats.Add(new NarrativeBeat
    {
        Sequence = 3,
        Kind = NarrativeBeatKinds.NpcDialogue,
        NpcName = "Abigail",
        SpeakerName = "Abigail",
        SpeakerDisplayName = "阿比盖尔",
        Text = "那就说定了。",
    });

    NarrativeEpisode latest = CreateEpisode("latest", 10, completed: false);
    latest.Beats.Add(new NarrativeBeat
    {
        Sequence = 1,
        Kind = NarrativeBeatKinds.NpcDialogue,
        NpcName = "Abigail",
        SpeakerName = "Abigail",
        SpeakerDisplayName = "阿比盖尔",
        Text = "我带好了剑。",
    });
    latest.Beats.Add(new NarrativeBeat
    {
        Sequence = 2,
        Kind = NarrativeBeatKinds.PlayerChoice,
        NpcName = "Abigail",
        SpeakerName = "Farmer",
        Text = "现在出发。",
    });
    latest.Beats.Add(new NarrativeBeat
    {
        Sequence = 3,
        Kind = NarrativeBeatKinds.NpcDialogue,
        NpcName = "Alex",
        SpeakerName = "Alex",
        SpeakerDisplayName = "亚历克斯",
        Text = "这句不属于阿比盖尔的连续记忆。",
    });

    var service = new NarrativeContextService();
    string context = service.Build(
        new[] { oldEpisode, latest },
        "Abigail",
        maximumCompleteEpisodes: 1,
        maximumEpisodeAnchors: 8,
        preferredMaximumCharacters: 2000);

    Assert(context.Contains("玩家选择“我会陪你一起去。”"), "Older player choice wasn't pinned.");
    Assert(context.Contains("最后记得“那就说定了。”"), "Older episode ending wasn't anchored.");
    Assert(context.Contains("我带好了剑。") && context.Contains("现在出发。"), "Latest episode wasn't kept whole.");
    Assert(!context.Contains("这句不属于阿比盖尔"), "Another NPC's unrelated beat leaked into target context.");
    Assert(context.Contains("进行中"), "Active episode status wasn't exposed to the prompt.");
}

static void TestNarrativeEpisodeForget()
{
    var store = new ConversationMemoryStore();
    List<NarrativeEpisode> episodes = store.GetNarrativeEpisodes("player");
    episodes.Add(CreateEpisode("abigail", 2, completed: true));
    NarrativeEpisode alex = CreateEpisode("alex", 3, completed: true);
    alex.ParticipantNames.Clear();
    alex.ParticipantNames.Add("Alex");
    alex.Beats.Clear();
    alex.Beats.Add(new NarrativeBeat { NpcName = "Alex", Kind = NarrativeBeatKinds.NpcDialogue, Text = "Hi" });
    episodes.Add(alex);

    int removed = store.ForgetNarrativeEpisodes("player", "Abigail");
    Assert(removed == 1, "Forgetting one NPC removed the wrong number of episodes.");
    Assert(episodes.Count == 1 && episodes[0].ParticipantNames.Contains("Alex"), "Forgetting one NPC damaged another archive.");
}

static NarrativeEpisode CreateEpisode(string eventId, int day, bool completed)
{
    return new NarrativeEpisode
    {
        EpisodeId = "episode-" + eventId,
        EventId = eventId,
        GameDate = $"Y1 spring {day}",
        TotalDays = day,
        StartedTimeOfDay = 900,
        LocationName = "Town",
        IsCompleted = completed,
        ParticipantNames = new List<string> { "Abigail" },
    };
}

static async Task TestRecentContextLimitAsync()
{
    var client = new RecordingClient("reply");
    var engine = new ConversationEngine(client);
    NpcConversationMemory memory = CreateMemory(10);

    ConversationEngineResult result = await engine.GenerateReplyAsync(
        "test-key",
        "system prompt",
        memory,
        "new message",
        "Y2 summer 4 1200",
        new ConversationEngineOptions
        {
            MaxContextMessages = 4,
            MaxOutputTokens = 777,
            SummaryTriggerMessageCount = 50,
            RecentMessagesToKeep = 2,
        });

    Assert(client.Requests.Count == 1, "Expected exactly one normal chat request.");
    DeepSeekChatRequest request = client.Requests[0];
    Assert(request.MaxTokens == 777, "MaxOutputTokens wasn't copied to max_tokens.");
    Assert(request.Messages.Count == 6, "Chat should contain system + 4 recent messages + new user message.");
    Assert(request.Messages[1].Content.Contains("message-6"), "Old messages weren't trimmed from context.");
    Assert(request.Messages[1].Content.StartsWith("[Y1 spring 7]"), "Stored game date wasn't added to recent context.");
    Assert(result.UpdatedMemory.Messages.Count == 12, "Successful turn wasn't appended to memory.");
}

static async Task TestBoundedCompactionBatchAsync()
{
    var client = new RecordingClient("updated summary", "reply");
    var engine = new ConversationEngine(client);

    ConversationEngineResult result = await engine.GenerateReplyAsync(
        "test-key",
        "system prompt",
        CreateMemory(20),
        "new message",
        "Y2 fall 1 900",
        new ConversationEngineOptions
        {
            MaxContextMessages = 4,
            MaxOutputTokens = 1024,
            SummaryTriggerMessageCount = 4,
            RecentMessagesToKeep = 2,
        });

    Assert(client.Requests.Count == 2, "Compaction should make one summary request and one chat request.");
    string summaryInput = client.Requests[0].Messages[1].Content;
    Assert(summaryInput.Contains("message-0") && summaryInput.Contains("message-3"), "Summary batch missed expected oldest messages.");
    Assert(!summaryInput.Contains("message-4"), "Summary batch exceeded MaxContextMessages.");
    Assert(result.Compaction.PrunedMessageCount == 4, "Unexpected compacted message count.");
    Assert(result.UpdatedMemory.Messages.Count == 18, "Compaction should retain 16 old messages and append a new pair.");
}

static async Task TestStreamingReplyAsync()
{
    var client = new RecordingClient("streamed reply");
    var engine = new ConversationEngine(client);
    var received = new List<DeepSeekStreamChunk>();

    ConversationEngineResult result = await engine.GenerateReplyStreamingAsync(
        "test-key",
        "system prompt",
        CreateMemory(2),
        "new message",
        "Y2 summer 5 1300",
        received.Add,
        new ConversationEngineOptions
        {
            MaxContextMessages = 4,
            MaxOutputTokens = 512,
            SummaryTriggerMessageCount = 50,
            RecentMessagesToKeep = 2,
        });

    Assert(client.Requests.Count == 1 && client.Requests[0].Stream, "Streaming request didn't set stream=true.");
    Assert(received.Any(chunk => chunk.ReasoningDelta.Length > 0), "Reasoning stream wasn't forwarded.");
    Assert(string.Concat(received.Select(chunk => chunk.ContentDelta)) == "streamed reply", "Content deltas weren't forwarded in order.");
    Assert(result.Reply == "streamed reply", "Streaming final reply wasn't returned.");
}

static async Task TestSummaryFailureFallbackAsync()
{
    var client = new RecordingClient(new InvalidOperationException("summary failed"), "reply");
    var engine = new ConversationEngine(client);

    ConversationEngineResult result = await engine.GenerateReplyAsync(
        "test-key",
        "system prompt",
        CreateMemory(20),
        "new message",
        "Y2 winter 2 1000",
        new ConversationEngineOptions
        {
            MaxContextMessages = 4,
            MaxOutputTokens = 1024,
            SummaryTriggerMessageCount = 4,
            RecentMessagesToKeep = 2,
        });

    Assert(result.Compaction.ContinuedAfterSummaryFailure, "Summary failure wasn't reported as a fallback.");
    Assert(client.Requests.Count == 2, "Normal chat should continue after summary failure.");
    Assert(client.Requests[1].Messages.Count == 6, "Fallback chat context wasn't bounded.");
    Assert(result.UpdatedMemory.Messages.Count == 22, "Failed compaction should preserve full stored history.");
}

static async Task TestSummaryLengthLimitAsync()
{
    var client = new RecordingClient(new string('x', 7000), "reply");
    var engine = new ConversationEngine(client);

    ConversationEngineResult result = await engine.GenerateReplyAsync(
        "test-key",
        "system prompt",
        CreateMemory(8),
        "new message",
        "Y3 spring 1 700",
        new ConversationEngineOptions
        {
            MaxContextMessages = 4,
            MaxOutputTokens = 1024,
            SummaryTriggerMessageCount = 4,
            RecentMessagesToKeep = 2,
        });

    Assert(result.UpdatedMemory.Summary.Length == 6001, "Long summary wasn't capped with an ellipsis.");
}

static void TestNarrativeStoreIsolation()
{
    var store = new NarrativeSaveStore();
    Assert(store.SchemaVersion == 3, "Narrative save schema wasn't upgraded for branch choices.");
    NpcNarrativeState first = store.GetOrCreate("1", "Abigail");
    NpcNarrativeState sameNpc = store.GetOrCreate("1", "abigail");
    NpcNarrativeState otherPlayer = store.GetOrCreate("2", "Abigail");

    Assert(ReferenceEquals(first, sameNpc), "NPC narrative state wasn't case-insensitive for one player.");
    Assert(!ReferenceEquals(first, otherPlayer), "Narrative state leaked across players.");
    Assert(store.TryGet("1", "ABIGAIL", out NpcNarrativeState? found) && ReferenceEquals(first, found), "Narrative store lookup failed.");
}

static void TestAuthoredAbigailArc()
{
    DirectoryInfo? root = new(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "VivantValley.csproj")))
        root = root.Parent;
    Assert(root is not null, "Couldn't locate the project root for authored story tests.");

    StoryCatalog catalog = StoryCatalog.LoadDirectory(Path.Combine(root!.FullName, "assets", "stories"));
    Assert(catalog.Count == 4, "The authored Abigail arc should contain four nodes.");
    Assert(catalog.Issues.Count == 0, "Authored story validation failed: " + string.Join(" | ", catalog.Issues));

    var planner = new PilotNarrativePlanner();
    var resolver = new NarrativeChoiceResolver();
    StoryDefinition opening = GetStory(catalog, "abigail.quartz-care.01");
    Assert(!opening.Repeatable && opening.Choices.Count == 3, "Opening node isn't a non-repeatable three-choice scene.");

    var adventureState = new NpcNarrativeState { PlayerId = "1", NpcName = "Abigail" };
    PlannedNpcEncounter openingEncounter = planner.CreateEncounter(opening, 1, 10, "recent chat");
    PlannedStoryChoice cherish = openingEncounter.Choices.Single(choice => choice.Id == "cherish");
    Assert(resolver.TryApply(adventureState, openingEncounter, cherish, 11, giftDelivered: true), "Opening adventure choice didn't resolve.");
    Assert(adventureState.Flags.Contains("abigail.arc.route-adventure"), "Adventure route flag wasn't applied.");
    Assert(adventureState.LastGiftDay == 11 && adventureState.CompletedStoryIds.Contains(opening.Id), "Opening gift completion wasn't recorded.");
    Assert(!resolver.TryApply(adventureState, openingEncounter, cherish, 11, giftDelivered: true), "The same story action resolved twice.");

    StoryDefinition cave = GetStory(catalog, cherish.NextStoryId);
    StoryDefinition rainy = GetStory(catalog, "abigail.rainy-challenge.02b");
    Assert(planner.CanEnterStory(adventureState, cave), "Adventure follow-up wasn't unlocked.");
    Assert(!planner.CanEnterStory(adventureState, rainy), "The unselected playful route was unlocked.");

    PlannedNpcEncounter caveEncounter = planner.CreateEncounter(cave, 1, 11, cherish.MemoryText);
    PlannedStoryChoice careful = caveEncounter.Choices.Single(choice => choice.Id == "careful");
    Assert(resolver.TryApply(adventureState, caveEncounter, careful, 13, giftDelivered: false), "Giftless cave choice didn't resolve.");
    Assert(adventureState.LastGiftDay == 11, "A giftless node changed LastGiftDay.");
    Assert(adventureState.Flags.Contains("abigail.arc.middle-complete"), "Middle completion flag wasn't applied.");

    StoryDefinition finale = GetStory(catalog, careful.NextStoryId);
    Assert(planner.CanEnterStory(adventureState, finale), "Finale wasn't unlocked after the adventure branch.");
    PlannedNpcEncounter finaleEncounter = planner.CreateEncounter(finale, 1, 13, careful.MemoryText);
    PlannedStoryChoice closeEnding = finaleEncounter.Choices.Single(choice => choice.Id == "stay-close");
    Assert(resolver.TryApply(adventureState, finaleEncounter, closeEnding, 15, giftDelivered: false), "Final relationship choice didn't resolve.");
    Assert(adventureState.Flags.Contains("abigail.arc.complete") && adventureState.Flags.Contains("abigail.arc.ending-close"), "Final ending flags weren't recorded.");
    Assert(!planner.CanEnterStory(adventureState, finale), "Completed finale became eligible again.");

    var playfulState = new NpcNarrativeState { PlayerId = "2", NpcName = "Abigail" };
    PlannedNpcEncounter playfulOpening = planner.CreateEncounter(opening, 1, 20, "recent chat");
    PlannedStoryChoice tease = playfulOpening.Choices.Single(choice => choice.Id == "tease");
    Assert(resolver.TryApply(playfulState, playfulOpening, tease, 21, giftDelivered: true), "Opening playful choice didn't resolve.");
    Assert(planner.CanEnterStory(playfulState, rainy) && !planner.CanEnterStory(playfulState, cave), "Playful route prerequisites didn't isolate the branch.");
}

static StoryDefinition GetStory(StoryCatalog catalog, string storyId)
{
    Assert(catalog.TryGet(storyId, out StoryDefinition? story) && story is not null, $"Missing story node {storyId}.");
    return story!;
}

static void TestStoryCatalogLoading()
{
    string directory = Path.Combine(Path.GetTempPath(), "stardew-ai-story-catalog-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(
            Path.Combine(directory, "story.json"),
            """
            {
              "id": "abigail.test.01",
              "version": 1,
              "npc": "Abigail",
              "priority": 10,
              "enabled": true,
              "repeatable": false,
              "trigger": { "minHearts": 2, "minConversationTurns": 1, "delayDays": 1, "expiryDays": 3, "cooldownDays": 4 },
              "scene": {
                "startTime": 900,
                "endTime": 2200,
                "activationDistanceTiles": 7,
                "giftItemId": "(O)80",
                "aiBrief": "Test authored brief.",
                "fallbackText": "Test fallback {GiftDisplayName}.",
                "acceptText": "Accept {GiftDisplayName}",
                "deferText": "Later"
              },
              "acceptEffects": { "trust": 2, "affection": 3, "setFlags": ["abigail.test-complete"] }
            }
            """);

        StoryCatalog catalog = StoryCatalog.LoadDirectory(directory);
        Assert(catalog.Count == 1 && catalog.Issues.Count == 0, "Valid story JSON wasn't loaded.");
        Assert(catalog.GetFirstForNpc("abigail")?.Id == "abigail.test.01", "NPC story lookup wasn't case-insensitive.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestDataDrivenStoryScheduling()
{
    var story = new StoryDefinition
    {
        Id = "abigail.test.01",
        Npc = "Abigail",
        Repeatable = false,
        Trigger = new StoryTriggerDefinition
        {
            MinHearts = 2,
            MinConversationTurns = 1,
            DelayDays = 2,
            ExpiryDays = 4,
            CooldownDays = 3,
            RequiredFlags = new HashSet<string>(StringComparer.Ordinal) { "intro.complete" },
        },
        Scene = new StorySceneDefinition
        {
            StartTime = 1000,
            EndTime = 2100,
            ActivationDistanceTiles = 6,
            GiftItemId = "(O)80",
            AiBrief = "Authored brief.",
            FallbackText = "Authored fallback.",
            AcceptText = "Accept",
            DeferText = "Later",
        },
        AcceptEffects = new StoryEffectsDefinition
        {
            Trust = 4,
            Affection = 5,
            SetFlags = new HashSet<string>(StringComparer.Ordinal) { "test.complete" },
        },
    };
    var planner = new PilotNarrativePlanner();
    var state = new NpcNarrativeState { LastEncounterDay = -1000 };
    Assert(!planner.CanSchedule(state, story, 1, 2, 10), "Story ignored a required flag.");
    state.Flags.Add("intro.complete");
    Assert(planner.CanSchedule(state, story, 1, 2, 10), "Eligible authored story wasn't selected.");

    PlannedNpcEncounter encounter = planner.CreateEncounter(story, 1, 10, "recent chat");
    Assert(encounter.StoryId == story.Id && encounter.EarliestDay == 12 && encounter.ExpiryDay == 15, "Story schedule wasn't snapshotted.");
    Assert(encounter.AiBrief == "Authored brief." && encounter.ActivationDistanceTiles == 6, "Story scene wasn't snapshotted.");
    Assert(encounter.TrustOnAccept == 4 && encounter.FlagsOnAccept.Contains("test.complete"), "Story effects weren't snapshotted.");

    state.CompletedStoryIds.Add(story.Id);
    Assert(!planner.CanSchedule(state, story, 2, 4, 20), "A completed non-repeatable story was scheduled again.");
}

static void TestPilotNarrativeScheduling()
{
    var planner = new PilotNarrativePlanner();
    var state = new NpcNarrativeState
    {
        PlayerId = "1",
        NpcName = "Abigail",
        LastEncounterDay = 2,
    };

    Assert(
        planner.CanSchedule(
            state,
            completedConversationTurns: 3,
            vanillaHearts: 2,
            currentDay: 10,
            minimumConversationTurns: 1,
            minimumHearts: 2,
            cooldownDays: 5),
        "Eligible Abigail conversation didn't schedule a proactive encounter.");

    PlannedNpcEncounter encounter = planner.CreateEncounter(
        "Abigail",
        sourceConversationTurn: 3,
        currentDay: 10,
        delayDays: 1,
        expiryDays: 5,
        giftItemId: "(O)80",
        triggerExcerpt: "I have been exhausted on the farm.");
    Assert(encounter.EarliestDay == 11 && encounter.ExpiryDay == 15, "Encounter window is incorrect.");
    Assert(!PilotNarrativePlanner.IsReady(encounter, 10, 1200), "Encounter became ready before its scheduled day.");
    Assert(PilotNarrativePlanner.IsReady(encounter, 11, 1200), "Encounter wasn't ready inside its window.");

    state.PendingEncounter = encounter;
    Assert(
        !planner.CanSchedule(state, 4, 4, 11, 1, 2, 5),
        "A second encounter was scheduled while a live action exists.");
}

static void TestPilotNarrativeDeferral()
{
    var planner = new PilotNarrativePlanner();
    PlannedNpcEncounter encounter = planner.CreateEncounter(
        "Abigail",
        sourceConversationTurn: 1,
        currentDay: 10,
        delayDays: 0,
        expiryDays: 1,
        giftItemId: "(O)80",
        triggerExcerpt: "hello",
        immediate: true);

    PilotNarrativePlanner.Defer(encounter, currentDay: 10, maximumAttempts: 3);
    Assert(encounter.Status == PlannedEncounterStatus.Deferred && encounter.EarliestDay == 11, "First defer didn't reschedule the action.");
    PilotNarrativePlanner.Defer(encounter, currentDay: 11, maximumAttempts: 3);
    PilotNarrativePlanner.Defer(encounter, currentDay: 12, maximumAttempts: 3);
    Assert(encounter.Status == PlannedEncounterStatus.Expired, "Repeated deferral didn't expire the action.");
    Assert(PilotNarrativePlanner.IsExpired(encounter, 12), "Expired action wasn't detected.");

    encounter.Status = PlannedEncounterStatus.Completed;
    Assert(!PilotNarrativePlanner.IsExpired(encounter, 99), "Completed action was incorrectly converted into expiry.");
}

static async Task TestProactiveScenePromptAsync()
{
    var client = new RecordingClient("Abigail scene line");
    var service = new ProactiveSceneService(client);
    var memory = CreateMemory(2);
    var encounter = new PlannedNpcEncounter
    {
        NpcName = "Abigail",
        TriggerExcerpt = "I have been exhausted on the farm.",
        GiftItemId = "(O)80",
        AiBrief = "Abigail brings a thoughtful gift after the player's difficult day.",
    };

    string result = await service.GenerateAsync(
        "test-key",
        "You are Abigail.",
        memory,
        encounter,
        "Quartz",
        new ConversationEngineOptions { MaxContextMessages = 8, MaxOutputTokens = 256 },
        CancellationToken.None);

    Assert(result == "Abigail scene line", "Proactive scene output wasn't returned.");
    Assert(client.Requests.Count == 1 && !client.Requests[0].Stream, "Proactive scene unexpectedly used streaming mode.");
    Assert(client.Requests[0].Messages.Any(message => message.Content.Contains("Quartz")), "Gift fact wasn't included in scene prompt.");
    Assert(client.Requests[0].Messages.Any(message => message.Content.Contains("thoughtful gift")), "Authored AI brief wasn't included in scene prompt.");
    Assert(client.Requests[0].Messages.Any(message => message.Content.Contains("exhausted on the farm")), "Recent chat excerpt wasn't included in scene prompt.");
}

static async Task TestProactiveSceneWithoutGiftPromptAsync()
{
    var client = new RecordingClient("Giftless scene line");
    var service = new ProactiveSceneService(client);
    var encounter = new PlannedNpcEncounter
    {
        NpcName = "Abigail",
        StoryId = "abigail.giftless.test",
        GiftItemId = string.Empty,
        AiBrief = "Abigail asks the player an important question.",
    };

    await service.GenerateAsync(
        "test-key",
        "You are Abigail.",
        CreateMemory(2),
        encounter,
        string.Empty,
        new ConversationEngineOptions { MaxContextMessages = 8, MaxOutputTokens = 256 },
        CancellationToken.None);

    Assert(client.Requests[0].Messages.Any(message => message.Content.Contains("不交付物品")), "Giftless story prompt still required a gift.");
}

static void TestDailySocialPlannerDeterminism()
{
    var planner = new DailySocialPlanner();
    SocialPlanningCandidate[] candidates =
    {
        CreateSocialCandidate("Abigail", valence: 0.8, lastProactiveDay: -1),
        CreateSocialCandidate("Haley", valence: 0.7, lastProactiveDay: -1),
        CreateSocialCandidate("Leah", valence: 0.6, lastProactiveDay: 90),
        CreateSocialCandidate("Penny", valence: 0.5, lastProactiveDay: -1),
        CreateSocialCandidate("Sam", valence: 0.4, lastProactiveDay: -1),
        CreateSocialCandidate("Sebastian", valence: 0.3, lastProactiveDay: -1),
        CreateSocialCandidate("Negative", valence: -0.8, lastProactiveDay: -1),
        CreateSocialCandidate("Cooldown", valence: 0.8, lastProactiveDay: 99),
    };

    DailySocialPlan first = planner.CreatePlan("save-1", "player-1", 100, candidates);
    DailySocialPlan reordered = planner.CreatePlan("save-1", "player-1", 100, candidates.Reverse());
    Assert(
        first.Seed == "fdebb9940334dc8578eddf7362375516f4509a1158b88aa02e387b61bbadec77",
        "The SHA-256 planner seed changed unexpectedly.");
    Assert(first.Seed == reordered.Seed, "Equivalent planning inputs produced different seeds.");
    Assert(
        first.Candidates.Select(candidate => candidate.NpcName)
            .SequenceEqual(reordered.Candidates.Select(candidate => candidate.NpcName)),
        "Candidate order changed when input enumeration order changed.");
    Assert(
        first.Candidates.Select(candidate => candidate.ActionId)
            .SequenceEqual(reordered.Candidates.Select(candidate => candidate.ActionId)),
        "Action IDs changed when input enumeration order changed.");
    int selectedNpcCount = first.Candidates
        .Select(candidate => candidate.NpcName)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    Assert(selectedNpcCount is >= 3 and <= 5, "Daily planning didn't select between three and five NPCs.");
    Assert(first.Candidates.Count == selectedNpcCount * 2, "Each selected NPC didn't receive two daily opportunities.");
    Assert(
        first.Candidates.GroupBy(candidate => candidate.NpcName, StringComparer.OrdinalIgnoreCase)
            .All(group => group.Select(candidate => candidate.TimeSlot).Distinct().Count() == 2),
        "A selected NPC didn't receive one morning and one evening opportunity.");
    Assert(
        first.Candidates.Select(candidate => candidate.ActionId).Distinct(StringComparer.Ordinal).Count()
        == first.Candidates.Count,
        "Daily candidates didn't receive unique deterministic action IDs.");

    DailySocialPlan nextDay = planner.CreatePlan("save-1", "player-1", 101, candidates);
    DailySocialPlan otherPlayer = planner.CreatePlan("save-1", "player-2", 100, candidates);
    Assert(nextDay.Seed != first.Seed, "Changing the day didn't change the planner seed.");
    Assert(otherPlayer.Seed != first.Seed, "Changing the player didn't change the planner seed.");
}

static void TestDailySocialPlannerEligibility()
{
    var planner = new DailySocialPlanner();
    SocialCandidateEvaluation negative = planner.EvaluateCandidate(
        CreateSocialCandidate("Negative", valence: -0.8, lastProactiveDay: -1),
        day: 100);
    SocialCandidateEvaluation recentlyProactive = planner.EvaluateCandidate(
        CreateSocialCandidate("RecentlyProactive", valence: 0.8, lastProactiveDay: 99),
        day: 100);
    SocialCandidateEvaluation positive = planner.EvaluateCandidate(
        CreateSocialCandidate("Positive", valence: 0.8, lastProactiveDay: 90),
        day: 100);

    Assert(
        !negative.IsEligible && negative.ExclusionReason == "latest_conversation_negative",
        "A negative latest conversation wasn't excluded.");
    Assert(
        recentlyProactive.IsEligible,
        "Yesterday's proactive encounter incorrectly blocked today's selection.");
    Assert(positive.IsEligible && positive.Score > 0d, "A recent positive conversation wasn't eligible.");

    DailySocialPlan plan = planner.CreatePlan(
        "save-1",
        "player-1",
        100,
        new[]
        {
            CreateSocialCandidate("Negative", valence: -0.8, lastProactiveDay: -1),
            CreateSocialCandidate("RecentlyProactive", valence: 0.8, lastProactiveDay: 99),
        });
    Assert(
        plan.Candidates.Count == 2
        && plan.Candidates.All(candidate => candidate.NpcName == "RecentlyProactive"),
        "A recent proactive encounter should still produce morning/evening opportunities.");
}

static SocialPlanningCandidate CreateSocialCandidate(string npcName, double valence, int lastProactiveDay)
{
    return new SocialPlanningCandidate
    {
        NpcName = npcName,
        VanillaHearts = 6,
        LastConversationDay = 99,
        LastProactiveDay = lastProactiveDay,
        RecentSignals = new List<ConversationSignal>
        {
            new()
            {
                Day = 99,
                ConversationTurn = 1,
                Valence = valence,
                Warmth = 0.8,
                Concern = 0.4,
                Confidence = 0.9,
                Topics = new List<string> { "mining" },
                OpenLoops = new List<string> { "follow_up" },
            },
        },
    };
}

static async Task TestConversationSignalExtractionAsync()
{
    var client = new RecordingClient(
        """
        {
          "valence": 0.75,
          "warmth": 0.8,
          "concern": 0.45,
          "topics": ["mining", "health"],
          "openLoops": ["player_is_tired"],
          "confidence": 0.9
        }
        """,
        "not-json",
        new InvalidOperationException("provider failed for test-key"));
    var extractor = new ConversationSignalExtractor(client);

    ConversationSignalExtractionResult parsed = await extractor.ExtractWithDiagnosticsAsync(
        "test-key",
        "Abigail",
        "I spent the day in the mines and feel tired.",
        "Please take care of yourself.",
        day: 42,
        conversationTurn: 7,
        new ConversationEngineOptions { MaxOutputTokens = 4096 });
    Assert(!parsed.UsedFallback, "Valid signal JSON unexpectedly used fallback.");
    Assert(parsed.Signal.Day == 42 && parsed.Signal.ConversationTurn == 7, "Signal metadata wasn't retained.");
    Assert(
        parsed.Signal.Valence == 0.75 && parsed.Signal.Topics.SequenceEqual(new[] { "mining", "health" }),
        "Signal JSON wasn't parsed.");
    Assert(client.Requests[0].MaxTokens == 512, "Signal classifier output wasn't bounded.");

    ConversationSignalExtractionResult malformed = await extractor.ExtractWithDiagnosticsAsync(
        "test-key",
        "Abigail",
        "hello",
        "hello",
        day: 43,
        conversationTurn: 8);
    Assert(malformed.UsedFallback, "Malformed signal JSON didn't use fallback.");
    Assert(
        malformed.Signal.Valence == 0d
        && malformed.Signal.Warmth == 0d
        && malformed.Signal.Confidence == 0d,
        "Malformed signal JSON didn't produce a neutral signal.");

    ConversationSignalExtractionResult providerFailure = await extractor.ExtractWithDiagnosticsAsync(
        "test-key",
        "Abigail",
        "hello",
        "hello",
        day: 44,
        conversationTurn: 9);
    Assert(providerFailure.UsedFallback, "Signal provider failure didn't use fallback.");
    Assert(
        !providerFailure.FailureReason.Contains("test-key", StringComparison.Ordinal),
        "Signal failure leaked the API key.");
}

static async Task TestAiSocialSceneContractAsync()
{
    var client = new RecordingClient(
        """{"dialogue":"I found this after our last chat.","action":"gift","giftCandidateId":"mining_quartz","motiveTag":"recent_mining"}""",
        "```json\n{\"dialogue\":\"This is fenced.\",\"action\":\"talk_only\",\"giftCandidateId\":null,\"motiveTag\":\"check_in\"}\n```",
        """{"dialogue":"Take this sword.","action":"gift","giftCandidateId":"(W)4","motiveTag":"help"}"""
    );
    var service = new AiSocialSceneService(client);
    var request = new AiSocialSceneRequest
    {
        NpcName = "Abigail",
        NpcDisplayName = "Abigail",
        GameContext = "Spring 10, sunny, six hearts.",
        RecentConversation = "The player talked about mining.",
        SignalSummary = "positive; topic=mining",
        ActivitySummary = "today: visit:mines=1",
        GiftCandidates = new[]
        {
            new SocialSceneGiftOption("mining_quartz", "Quartz", new[] { "mining" }),
        },
        FallbackDialogue = "I just wanted to see how you were doing.",
    };

    AiSocialSceneDecision valid = await service.GenerateAsync("test-key", request);
    Assert(!valid.UsedFallback, "Strict valid scene JSON unexpectedly used fallback.");
    Assert(
        valid.Action == SocialSceneActions.Gift && valid.GiftCandidateId == "mining_quartz",
        "An allowlisted scene gift wasn't accepted.");
    Assert(
        !client.Requests[0].Messages.Any(message => message.Content.Contains("(O)", StringComparison.Ordinal)),
        "The AI scene prompt exposed a qualified item ID.");

    AiSocialSceneDecision fenced = await service.GenerateAsync("test-key", request);
    Assert(
        fenced.UsedFallback && fenced.Action == SocialSceneActions.TalkOnly && fenced.GiftCandidateId is null,
        "Markdown-wrapped scene JSON wasn't rejected fail-closed.");

    AiSocialSceneDecision unauthorized = await service.GenerateAsync("test-key", request);
    Assert(
        unauthorized.UsedFallback
        && unauthorized.Action == SocialSceneActions.TalkOnly
        && unauthorized.GiftCandidateId is null,
        "An unauthorized AI gift didn't fall back to talk-only.");
}

static async Task TestNpcGiftToolContractAsync()
{
    var client = new RecordingClient(
        """{"tool":"give_gift","giftCandidateId":"safe","reasonTag":"warm_chat"}""",
        """{"tool":"mail_gift","giftCandidateId":"safe","reasonTag":"farm_delivery"}""",
        """{"tool":"give_gift","giftCandidateId":"(O)74","reasonTag":"invented"}""");
    var resolver = new FakeGiftItemResolver(new Dictionary<string, SocialGiftItemFacts>(StringComparer.Ordinal)
    {
        ["(O)safe"] = CreateGiftFacts("(O)safe", "Quartz", sellPrice: 80),
    });
    var policy = new GiftPolicyService(
        new SocialGiftPoolCatalog
        {
            Gifts = new List<SocialGiftPoolEntry> { CreateGiftEntry("safe", "(O)safe") },
        },
        resolver);
    var service = new NpcGiftToolService(client, policy);
    var request = new AiGiftToolRequest
    {
        NpcName = "Haley",
        NpcDisplayName = "Haley",
        GameContext = "Spring 10, sunny.",
        PlayerMessage = "That was a lovely conversation.",
        NpcReply = "今天天气真不错。",
        RecentConversation = "The conversation was warm.",
        ActivitySummary = "photography",
        GiftCandidates = new[]
        {
            new SocialSceneGiftOption("safe", "Quartz", new[] { "general" }),
        },
    };

    AiGiftToolDecision valid = await service.DecideAsync("test-key", request);
    Assert(valid.ShouldGiveGift && valid.GiftCandidateId == "safe", "A valid give_gift call wasn't accepted.");
    Assert(
        !client.Requests[0].Messages.Any(message => message.Content.Contains("(O)safe", StringComparison.Ordinal)),
        "The give_gift decision prompt exposed a qualified item ID.");
    Assert(
        !client.Requests[0].Messages.Any(message => message.Content.Contains(request.NpcReply, StringComparison.Ordinal)),
        "Gift planning still depended on the generated NPC reply.");

    AiGiftToolDecision mail = await service.DecideAsync("test-key", request);
    Assert(
        !mail.ShouldUseGiftTool && mail.UsedFallback,
        "Face-to-face planning still accepted mail_gift instead of reserving it for overnight planning.");

    AiGiftToolDecision unauthorized = await service.DecideAsync("test-key", request);
    Assert(
        !unauthorized.ShouldGiveGift && unauthorized.UsedFallback,
        "An invented give_gift candidate didn't fail closed.");

    AiGiftToolDecision noCandidates = await service.DecideAsync(
        "test-key",
        new AiGiftToolRequest { NpcName = "Haley" });
    Assert(!noCandidates.ShouldUseGiftTool && client.Requests.Count == 3, "An empty allowlist should skip gift planning.");

    var candidate = new SocialGiftCandidate { Key = "safe", DisplayName = "Quartz", Quantity = 2 };
    var immediate = new ConversationGiftExecutionResult
    {
        RequestedToolName = NpcGiftToolNames.GiveGift,
        Outcome = ConversationGiftOutcome.ImmediateDelivered,
        Candidate = candidate,
        Quantity = 2,
    };
    string immediatePrompt = service.BuildFinalResponsePrompt("NPC prompt", immediate);
    Assert(immediatePrompt.Contains("已经让玩家当面收到", StringComparison.Ordinal), "The final prompt didn't receive the committed immediate result.");
    string guardedImmediate = NpcGiftToolService.GuardVisibleReply(
        "我明天把礼物寄到你的邮箱。",
        immediate,
        "Haley",
        out bool immediateReplaced);
    Assert(immediateReplaced && guardedImmediate.Contains("当面递给你", StringComparison.Ordinal), "An immediate gift incorrectly allowed a mail claim.");
    string guardedWrongItem = NpcGiftToolService.GuardVisibleReply(
        "这包苹果树苗你拿着。",
        immediate,
        "Haley",
        out bool wrongItemReplaced);
    Assert(
        wrongItemReplaced
        && guardedWrongItem.Contains("Quartz ×2", StringComparison.Ordinal),
        "A dialogue/item mismatch wasn't rebound to the resolved gift.");
    string guardedExactItem = NpcGiftToolService.GuardVisibleReply(
        "这两块 Quartz 送给你，希望能派上用场。",
        immediate,
        "Haley",
        out bool exactItemReplaced);
    Assert(!exactItemReplaced, "A dialogue containing the resolved gift name was replaced unnecessarily.");

    var snack = new SocialGiftCandidate { Key = "snack", DisplayName = "Field Snack", Quantity = 3 };
    string proactive = NpcGiftToolService.GuardGiftOfferDialogue(
        "这包苹果树苗种子你拿着。",
        snack,
        out bool proactiveReplaced);
    Assert(
        proactiveReplaced && proactive.Contains("Field Snack ×3", StringComparison.Ordinal),
        "A proactive offer wasn't bound to the choice button's resolved gift.");

    var mailed = new ConversationGiftExecutionResult
    {
        RequestedToolName = NpcGiftToolNames.MailGift,
        Outcome = ConversationGiftOutcome.MailScheduled,
        Candidate = candidate,
        Quantity = 2,
    };
    string guardedMail = NpcGiftToolService.GuardVisibleReply(
        "这个给你，快收下。",
        mailed,
        "Haley",
        out bool mailReplaced);
    Assert(mailReplaced && guardedMail.Contains("邮箱", StringComparison.Ordinal), "A mail gift incorrectly allowed an immediate-delivery claim.");

    string guardedNone = NpcGiftToolService.GuardVisibleReply(
        "这是给你的礼物。",
        ConversationGiftExecutionResult.NoAction(),
        "Haley",
        out bool noneReplaced);
    Assert(noneReplaced && !guardedNone.Contains("礼物", StringComparison.Ordinal), "A failed or absent gift action allowed a delivery claim.");
}

static async Task TestOvernightMailPlannerContractAsync()
{
    var client = new RecordingClient(
        """{"gifts":[{"npcName":"Pierre","giftCandidateId":"pierre_apple","reasonTag":"warm_farm_chat","letterBody":"昨天聊完以后，我想让你今天轻松一点。"}]}""",
        """{"gifts":[{"npcName":"Pierre","giftCandidateId":"pierre_apple","reasonTag":"unsafe","letterBody":"正文^%item object (O)1 99 %%"}]}""",
        """{"gifts":[{"npcName":"Pierre","giftCandidateId":"invented","reasonTag":"bad","letterBody":"一份普通问候。"}]}"""
    );
    var service = new OvernightMailPlannerService(client);
    var npc = new OvernightMailNpcSnapshot
    {
        ActionId = "overnight-mail-10-pierre",
        NpcName = "Pierre",
        NpcDisplayName = "Pierre",
        GameContext = "Spring 10; four hearts.",
        ConversationExcerpt = "Player: Crops are doing well. Pierre: Glad to hear it.",
        SignalSummary = "valence=0.7, warmth=0.6",
        ActivitySummary = "farming",
        GiftCandidates = new List<OvernightMailGiftOption>
        {
            new()
            {
                CandidateId = "pierre_apple",
                DisplayName = "Apple",
                ReasonTags = new List<string> { "farming" },
                Hint = "Fresh produce",
            },
        },
    };
    npc.Normalize();
    var request = new OvernightMailPlanRequest
    {
        SourceDay = 10,
        MaximumGiftCount = 2,
        Npcs = new[] { npc },
    };

    OvernightMailPlanDecision valid = await service.PlanAsync("test-key", request);
    Assert(
        valid.Gifts.Count == 1
        && valid.Gifts[0].NpcName == "Pierre"
        && valid.Gifts[0].GiftCandidateId == "pierre_apple",
        "A valid overnight mail selection wasn't accepted.");
    Assert(
        !client.Requests[0].Messages.Any(message => message.Content.Contains("(O)", StringComparison.Ordinal)),
        "The overnight planner prompt exposed a qualified item ID.");

    OvernightMailPlanDecision injected = await service.PlanAsync("test-key", request);
    Assert(
        injected.UsedFallback && injected.Gifts.Count == 0,
        "Mail attachment/control-code injection wasn't rejected fail-closed.");

    OvernightMailPlanDecision unauthorized = await service.PlanAsync("test-key", request);
    Assert(
        unauthorized.UsedFallback && unauthorized.Gifts.Count == 0,
        "An unauthorized overnight gift candidate wasn't rejected fail-closed.");

    var negative = new ConversationSignal
    {
        Day = 10,
        ConversationTurn = 1,
        Valence = -0.9,
        Warmth = 0.05,
        Concern = 0.1,
        Confidence = 0.9,
    };
    var positive = new ConversationSignal
    {
        Day = 10,
        ConversationTurn = 2,
        Valence = 0.7,
        Warmth = 0.7,
        Confidence = 0.9,
    };
    ConversationSignal neutralPending = ConversationSignalExtractor.CreateNeutral(10, 3);
    Assert(
        !OvernightMailPlannerService.IsEligibleConversation(new[] { negative }, 0.35),
        "A clearly negative conversation remained eligible for overnight mail.");
    Assert(
        OvernightMailPlannerService.IsEligibleConversation(new[] { positive }, 0.35),
        "A positive conversation wasn't eligible for overnight mail.");
    Assert(
        OvernightMailPlannerService.IsEligibleConversation(new[] { neutralPending }, 0.35),
        "An unfinished signal analysis incorrectly erased a real completed chat.");
}

static void TestMailGiftPersistenceModel()
{
    var player = new PlayerSocialDirectorState
    {
        PlayerId = "1",
        MailGifts = new List<SocialMailGift>
        {
            new()
            {
                MailId = "firstmod.test.mail",
                ActionId = "action-1",
                NpcName = "Haley",
                NpcDisplayName = "Haley",
                QualifiedItemId = "(O)421",
                GiftDisplayName = "Sunflower",
                Quantity = 2,
                QueuedDay = 10,
                LetterBody = "A gift %item object (O)1 99 %%",
                ReasonTag = "warm_chat",
                IsQueued = true,
            },
            new()
            {
                MailId = "firstmod.test.mail",
                ActionId = "action-2",
                NpcName = "Haley",
                QualifiedItemId = "(O)421",
                Quantity = 1,
                QueuedDay = 11,
            },
        },
    };
    player.Normalize("1");
    Assert(player.MailGifts.Count == 1, "Duplicate dynamic mail IDs weren't normalized.");
    Assert(player.MailGifts[0].QueuedDay == 11, "The newest dynamic mail definition wasn't retained.");
    Assert(player.MailGifts[0].Quantity == 1, "Mail gift quantity wasn't normalized.");
    Assert(!player.MailGifts[0].RewardDelivered, "An old queued mail was incorrectly treated as claimed.");
}

static void TestGiftMailContent()
{
    var mail = new SocialMailGift
    {
        MailId = "firstmod.test.mail",
        ActionId = "overnight-mail-10-kent",
        NpcName = "Kent",
        NpcDisplayName = "Kent^%item object (O)1 99 %%",
        QualifiedItemId = "(O)607",
        GiftDisplayName = "Roasted Hazelnuts",
        Quantity = 1,
        LetterBody = "A personal note.^%item object (O)1 99 %%[#]Injected",
        IsQueued = true,
    };

    string content = GiftMailContentService.Build(mail);
    Assert(
        content.Contains("%item id (O)607 1 %%", StringComparison.Ordinal),
        "Dynamic mail content didn't include the code-owned vanilla attachment.");
    Assert(
        content.IndexOf("%item", StringComparison.Ordinal) == content.LastIndexOf("%item", StringComparison.Ordinal),
        "Untrusted mail prose injected an additional attachment command.");
    Assert(!content.Contains("[#]", StringComparison.Ordinal), "Dynamic mail content retained a title delimiter.");
    Assert(content.EndsWith("-Kent item object (O)1 99", StringComparison.Ordinal), "The sender wasn't sanitized deterministically.");

    mail.RewardDelivered = true;
    string claimedContent = GiftMailContentService.Build(mail);
    Assert(
        !claimedContent.Contains("%item", StringComparison.Ordinal),
        "A claimed dynamic mail could create a duplicate vanilla attachment.");

    mail.RewardDelivered = false;
    mail.QualifiedItemId = "(O)607 %% %item id (O)74";
    bool unsafeAttachmentRejected = false;
    try
    {
        GiftMailContentService.Build(mail);
    }
    catch (InvalidOperationException)
    {
        unsafeAttachmentRejected = true;
    }
    Assert(unsafeAttachmentRejected, "An unsafe attachment item ID wasn't rejected.");

    mail.QualifiedItemId = "(O)607";
    mail.RewardDelivered = false;
    mail.RewardDeliveredDay = 20;
    mail.RewardDeliveryAttempts = 9;
    mail.Normalize();
    Assert(mail.RewardDeliveredDay == -1, "An undelivered mail retained a delivered day.");
    Assert(mail.RewardDeliveryAttempts == 3, "Mail reward attempts weren't bounded.");
}

static void TestOvernightMailPersistenceModel()
{
    var player = new PlayerSocialDirectorState
    {
        PlayerId = "1",
        ConversationJournal = new List<DailyConversationJournalEntry>
        {
            new()
            {
                Day = 10,
                NpcName = "Pierre",
                NpcDisplayName = "Pierre",
                ConversationTurn = 4,
                PlayerExcerpt = "Crops are growing well.",
                NpcExcerpt = "That's good to hear.",
            },
            new()
            {
                Day = 10,
                NpcName = "pierre",
                ConversationTurn = 4,
                PlayerExcerpt = "newer duplicate",
                NpcExcerpt = "newer duplicate",
            },
        },
        PendingOvernightMailPlan = new OvernightMailPlanSnapshot
        {
            PlanId = "overnight-mail-plan-10",
            SourceDay = 10,
            DeliverOnOrAfterDay = 11,
            Npcs = new List<OvernightMailNpcSnapshot>
            {
                new()
                {
                    ActionId = "overnight-mail-10-pierre",
                    NpcName = "Pierre",
                    NpcDisplayName = "Pierre",
                    GiftCandidates = new List<OvernightMailGiftOption>
                    {
                        new() { CandidateId = "pierre_apple", DisplayName = "Apple" },
                    },
                },
            },
        },
    };

    player.Normalize("1");
    Assert(player.ConversationJournal.Count == 1, "Duplicate daily conversation journal entries weren't normalized.");
    Assert(player.PendingOvernightMailPlan is not null, "A valid pending overnight plan wasn't preserved.");
    Assert(
        player.PendingOvernightMailPlan!.DeliverOnOrAfterDay == 11
        && player.PendingOvernightMailPlan.Npcs.Count == 1,
        "A pending overnight plan wasn't normalized for save/load recovery.");
}

static void TestGiftPolicySafety()
{
    var resolver = new FakeGiftItemResolver(new Dictionary<string, SocialGiftItemFacts>(StringComparer.Ordinal)
    {
        ["(O)value20"] = CreateGiftFacts("(O)value20", "Value 20", sellPrice: 20),
        ["(O)value80"] = CreateGiftFacts("(O)value80", "Value 80", sellPrice: 80),
        ["(O)value100"] = CreateGiftFacts("(O)value100", "Value 100", sellPrice: 100),
        ["(O)value5000"] = CreateGiftFacts("(O)value5000", "Value 5000", sellPrice: 5000),
        ["(O)tool"] = CreateGiftFacts("(O)tool", "Unsafe Tool", sellPrice: 100, isTool: true),
        ["(O)quest"] = CreateGiftFacts("(O)quest", "Quest Item", sellPrice: 100, isQuestOrUnique: true),
        ["(O)nonobject"] = CreateGiftFacts("(O)nonobject", "Not Object", sellPrice: 50, isObject: false),
    });
    var catalog = new SocialGiftPoolCatalog
    {
        Gifts = new List<SocialGiftPoolEntry>
        {
            CreateGiftEntry("value20", "(O)value20"),
            CreateGiftEntry("value80", "(O)value80"),
            CreateGiftEntry("value100", "(O)value100"),
            CreateGiftEntry("value5000", "(O)value5000"),
            CreateGiftEntry("tool", "(O)tool"),
            CreateGiftEntry("quest", "(O)quest"),
            CreateGiftEntry("nonobject", "(O)nonobject"),
        },
    };
    var service = new GiftPolicyService(
        catalog,
        resolver,
        new SocialGiftPolicyOptions
        {
            MaximumCandidateCount = 8,
        });
    GiftPolicyContext allowedContext = CreateGiftContext();

    SocialGiftCandidateSet candidates = service.BuildCandidateSet(allowedContext);
    Assert(candidates.CanOfferGift && candidates.Candidates.Count == 4, "Safe gift allowlist contained unexpected entries.");
    Assert(candidates.Candidates.Single(candidate => candidate.Key == "value20").Quantity == 5, "A 20g gift wasn't batched to 5 items.");
    Assert(candidates.Candidates.Single(candidate => candidate.Key == "value80").Quantity == 2, "An 80g gift wasn't batched to 2 items.");
    Assert(candidates.Candidates.Single(candidate => candidate.Key == "value100").Quantity == 1, "A 100g gift should remain a single item.");
    Assert(candidates.Candidates.Single(candidate => candidate.Key == "value5000").Quantity == 1, "A 5000g gift should be allowed as one item.");
    Assert(
        candidates.Rejections.Any(rejection => rejection.Reason == SocialGiftRejectionReason.ToolOrWeapon),
        "Tool gift wasn't rejected.");
    Assert(
        candidates.Rejections.Any(rejection => rejection.Reason == SocialGiftRejectionReason.QuestOrUniqueItem),
        "Quest gift wasn't rejected.");
    Assert(
        candidates.Rejections.Any(rejection => rejection.Reason == SocialGiftRejectionReason.NonObjectItem),
        "Non-object gift wasn't rejected.");

    SocialGiftSelectionResult safe = service.ValidateAiSelection(allowedContext, "value80");
    SocialGiftSelectionResult invented = service.ValidateAiSelection(allowedContext, "(O)74");
    Assert(
        safe.Kind == SocialGiftSelectionKind.Gift
        && safe.Candidate?.QualifiedItemId == "(O)value80"
        && safe.Candidate.Quantity == 2,
        "Safe gift selection wasn't approved.");
    Assert(
        invented.RejectionReason == SocialGiftRejectionReason.UnknownCandidateKey,
        "An invented item ID wasn't rejected.");

    SocialGiftCandidateSet alreadyOffered = service.BuildCandidateSet(CreateGiftContext(giftAlreadyOfferedToday: true));
    SocialGiftCandidateSet duplicate = service.BuildCandidateSet(
        CreateGiftContext(completedActionIds: new[] { "action-1" }));
    Assert(
        alreadyOffered.BlockReason == SocialGiftRejectionReason.NpcAlreadyOfferedToday,
        "The per-NPC daily gift offer limit wasn't enforced.");
    Assert(
        duplicate.BlockReason == SocialGiftRejectionReason.DuplicateActionId,
        "Duplicate action ID wasn't rejected.");
}

static void TestGiftPolicySchemaV2()
{
    var resolver = new FakeGiftItemResolver(new Dictionary<string, SocialGiftItemFacts>(StringComparer.Ordinal)
    {
        ["(O)shared"] = CreateGiftFacts("(O)shared", "Shared Snack", sellPrice: 20),
        ["(O)abigail"] = CreateGiftFacts("(O)abigail", "Amethyst", sellPrice: 100),
        ["(O)haley"] = CreateGiftFacts("(O)haley", "Sunflower", sellPrice: 80),
        ["(O)legacy"] = CreateGiftFacts("(O)legacy", "Legacy Cookie", sellPrice: 50),
    });
    const string catalogJson = """
        {
          "schemaVersion": 2,
          "items": [
            {
              "key": "shared",
              "qualifiedItemId": "(O)shared",
              "displayHint": "A small snack for anyone.",
              "applicableTags": ["general"],
              "category": "fallback",
              "repeatCooldownDays": 2,
              "deliveryModes": ["immediate", "mail"]
            },
            {
              "key": "signature",
              "qualifiedItemId": "(O)abigail",
              "displayHint": "Abigail's personal choice.",
              "applicableTags": ["general"],
              "category": "signature",
              "minHearts": 4,
              "repeatCooldownDays": 10,
              "deliveryModes": ["immediate"]
            },
            {
              "key": "mail_flower",
              "qualifiedItemId": "(O)haley",
              "displayHint": "Haley sends this by post.",
              "applicableTags": ["general"],
              "category": "care",
              "deliveryModes": ["mail"]
            }
          ],
          "global": ["shared"],
          "npcPools": {
            "Abigail": ["signature"],
            "Haley": ["mail_flower"]
          },
          "gifts": [
            {
              "key": "legacy_cookie",
              "qualifiedItemId": "(O)legacy",
              "displayHint": "A schema-v1 entry retained in a v2 catalog.",
              "npcNames": ["Abigail"],
              "applicableTags": ["general"],
              "category": "care",
              "deliveryModes": ["immediate"]
            }
          ]
        }
        """;
    GiftPolicyService service = GiftPolicyService.LoadFromJson(
        catalogJson,
        resolver,
        new SocialGiftPolicyOptions { MaximumCandidateCount = 8 });

    SocialGiftCandidateSet abigail = service.BuildCandidateSet(
        CreateSchemaV2GiftContext(
            "Abigail",
            heartLevel: 6,
            deliveryMode: SocialGiftDeliveryModes.Immediate));
    string[] abigailKeys = abigail.Candidates.Select(candidate => candidate.Key).ToArray();
    Assert(abigailKeys.Contains("global_shared"), "A schema-v2 global template wasn't expanded.");
    Assert(abigailKeys.Contains("abigail_signature"), "Abigail's schema-v2 pool wasn't expanded.");
    Assert(!abigailKeys.Contains("haley_mail_flower"), "Haley's pool leaked into Abigail's candidates.");
    SocialGiftCandidate abigailSignature = abigail.Candidates.Single(candidate => candidate.Key == "abigail_signature");
    SocialGiftCandidate sharedGift = abigail.Candidates.Single(candidate => candidate.Key == "global_shared");
    Assert(
        abigailSignature.QualifiedItemId == "(O)abigail"
        && abigailSignature.Category == SocialGiftCategories.Signature
        && abigailSignature.MinHearts == 4
        && abigailSignature.RepeatCooldownDays == 3,
        "A non-ordinary schema-v2 gift did not receive the three-day cooldown.");
    Assert(sharedGift.RepeatCooldownDays == 2, "An ordinary fallback gift did not preserve its catalog cooldown.");

    SocialGiftCandidateSet haley = service.BuildCandidateSet(
        CreateSchemaV2GiftContext(
            "Haley",
            heartLevel: 6,
            deliveryMode: SocialGiftDeliveryModes.Mail));
    string[] haleyKeys = haley.Candidates.Select(candidate => candidate.Key).ToArray();
    Assert(haleyKeys.Contains("global_shared") && haleyKeys.Contains("haley_mail_flower"), "Haley's pool wasn't isolated and expanded.");
    Assert(!haleyKeys.Contains("abigail_signature"), "Abigail's pool leaked into Haley's candidates.");

    SocialGiftCandidateSet lowHearts = service.BuildCandidateSet(
        CreateSchemaV2GiftContext(
            "Abigail",
            heartLevel: 3,
            deliveryMode: SocialGiftDeliveryModes.Immediate));
    Assert(
        !lowHearts.Candidates.Any(candidate => candidate.Key == "abigail_signature")
        && lowHearts.Rejections.Any(rejection =>
            rejection.Key == "abigail_signature"
            && rejection.Reason == SocialGiftRejectionReason.RelationshipTooLow),
        "A schema-v2 minHearts requirement wasn't enforced.");

    SocialGiftCandidateSet wrongDelivery = service.BuildCandidateSet(
        CreateSchemaV2GiftContext(
            "Abigail",
            heartLevel: 6,
            deliveryMode: SocialGiftDeliveryModes.Mail));
    Assert(
        !wrongDelivery.Candidates.Any(candidate => candidate.Key == "abigail_signature")
        && wrongDelivery.Rejections.Any(rejection =>
            rejection.Key == "abigail_signature"
            && rejection.Reason == SocialGiftRejectionReason.DeliveryModeNotAllowed),
        "A schema-v2 deliveryModes restriction wasn't enforced.");

    var repeatResolver = new FakeGiftItemResolver(new Dictionary<string, SocialGiftItemFacts>(StringComparer.Ordinal)
    {
        ["(O)recent"] = CreateGiftFacts("(O)recent", "Recent Gift", sellPrice: 100),
        ["(O)fresh"] = CreateGiftFacts("(O)fresh", "Fresh Gift", sellPrice: 100),
    });
    var repeatService = new GiftPolicyService(
        new SocialGiftPoolCatalog
        {
            SchemaVersion = 2,
            Items = new List<SocialGiftItemTemplate>
            {
                CreateGiftTemplate("recent", "(O)recent", SocialGiftCategories.Seasonal, priority: 100),
                CreateGiftTemplate("fresh", "(O)fresh", SocialGiftCategories.Seasonal),
            },
            Global = new List<string> { "recent", "fresh" },
        },
        repeatResolver);
    SocialGiftCandidateSet repeatCandidates = repeatService.BuildCandidateSet(
        CreateSchemaV2GiftContext(
            "Abigail",
            heartLevel: 10,
            deliveryMode: SocialGiftDeliveryModes.Immediate,
            recentGifts: new[] { new NpcGiftHistoryEntry { QualifiedItemId = "(O)recent", Day = 98 } }));
    string[] repeatKeys = repeatCandidates.Candidates.Select(candidate => candidate.Key).ToArray();
    Assert(
        repeatKeys.Contains("global_fresh")
        && !repeatKeys.Contains("global_recent")
        && repeatCandidates.Rejections.Any(rejection =>
            rejection.Key == "global_recent"
            && rejection.Reason == SocialGiftRejectionReason.RecentlyGiven),
        "A gift inside its repeat window wasn't excluded with the expected reason.");

    string[] signatureKeys = { "sig1", "sig2", "sig3", "sig4" };
    string[] activityKeys = { "activity1", "activity2" };
    var quotaFacts = signatureKeys.Concat(activityKeys).ToDictionary(
        key => $"(O){key}",
        key => CreateGiftFacts($"(O){key}", key, sellPrice: 100),
        StringComparer.Ordinal);
    var quotaCatalog = new SocialGiftPoolCatalog
    {
        SchemaVersion = 2,
        Items = signatureKeys
            .Select((key, index) => CreateGiftTemplate(key, $"(O){key}", SocialGiftCategories.Signature, priority: 100 - index))
            .Concat(activityKeys.Select(key => CreateGiftTemplate(key, $"(O){key}", SocialGiftCategories.Activity)))
            .ToList(),
        Global = signatureKeys.Concat(activityKeys).ToList(),
    };
    var quotaService = new GiftPolicyService(
        quotaCatalog,
        new FakeGiftItemResolver(quotaFacts),
        new SocialGiftPolicyOptions { MaximumCandidateCount = 4 });
    SocialGiftCandidateSet quotaCandidates = quotaService.BuildCandidateSet(
        CreateSchemaV2GiftContext(
            "Abigail",
            heartLevel: 10,
            deliveryMode: SocialGiftDeliveryModes.Immediate));
    Assert(quotaCandidates.Candidates.Count == 4, "The schema-v2 category quota test didn't fill the candidate budget.");
    Assert(
        quotaCandidates.Candidates.Count(candidate => candidate.Category == SocialGiftCategories.Signature) == 2
        && quotaCandidates.Candidates.Count(candidate => candidate.Category == SocialGiftCategories.Activity) == 2,
        "A high-priority category displaced candidates beyond its category quota.");

    Assert(
        abigailKeys.Contains("legacy_cookie"),
        "A schema-v2 catalog didn't retain compatible legacy gifts entries.");
}

static void TestProductionGiftCatalog()
{
    string path = FindRepositoryFile(Path.Combine("assets", "social", "gift-pools.json"));
    string json = File.ReadAllText(path);
    SocialGiftPoolCatalog? catalog = JsonSerializer.Deserialize<SocialGiftPoolCatalog>(
        json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    Assert(catalog is not null && catalog.SchemaVersion == 2, "The production gift catalog isn't schema v2.");

    string[] expectedNpcs =
    {
        "Abigail", "Alex", "Caroline", "Clint", "Demetrius", "Dwarf", "Elliott", "Emily",
        "Evelyn", "George", "Gus", "Haley", "Harvey", "Jas", "Jodi", "Kent", "Krobus",
        "Leah", "Leo", "Lewis", "Linus", "Marnie", "Maru", "Pam", "Penny", "Pierre",
        "Robin", "Sam", "Sandy", "Sebastian", "Shane", "Vincent", "Willy", "Wizard",
    };
    Assert(
        catalog!.NpcPools.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(expectedNpcs.OrderBy(value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
        "The production catalog doesn't cover the complete social NPC roster.");
    Assert(
        catalog.NpcPools.All(pool => pool.Value.Count == 12),
        "Every production NPC pool must contain exactly 12 authored references.");

    var facts = catalog.Items
        .Select(item => item.QualifiedItemId)
        .Concat(catalog.Gifts.Select(item => item.QualifiedItemId))
        .Where(itemId => !string.IsNullOrWhiteSpace(itemId))
        .Distinct(StringComparer.Ordinal)
        .ToDictionary(
            itemId => itemId,
            itemId => CreateGiftFacts(itemId, itemId, sellPrice: 100),
            StringComparer.Ordinal);
    GiftPolicyService service = GiftPolicyService.LoadFromJson(
        json,
        new FakeGiftItemResolver(facts),
        new SocialGiftPolicyOptions { MaximumCandidateCount = 12 });
    Assert(
        service.CatalogIssues.Count == 0,
        "The production gift catalog has normalization issues: " + string.Join(" | ", service.CatalogIssues));

    string[] allTags = catalog.Items
        .SelectMany(item => item.ApplicableTags)
        .Append("general")
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    foreach (string npcName in expectedNpcs)
    {
        SocialGiftCandidateSet candidates = service.BuildCandidateSet(new GiftPolicyContext
        {
            ActionId = "production-catalog-" + npcName,
            NpcName = npcName,
            CurrentDay = 100,
            RelevantTags = allTags,
            HeartLevel = 14,
            DeliveryMode = SocialGiftDeliveryModes.Immediate,
        });
        Assert(
            candidates.CanOfferGift && candidates.Candidates.Count == 12,
            $"The production pool for {npcName} didn't expose 12 safe balanced candidates.");
    }
}

static string FindRepositoryFile(string relativePath)
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
        string candidate = Path.Combine(current.FullName, relativePath);
        if (File.Exists(candidate))
            return candidate;
        current = current.Parent;
    }

    throw new FileNotFoundException($"Couldn't locate repository file {relativePath} from {AppContext.BaseDirectory}.");
}

static void TestConversationContinuationPolicy()
{
    var target = new ConversationContinuationTarget(
        "player-1",
        "Abigail",
        "Abigail",
        100);
    ConversationContinuationBlockReason allowed = ConversationContinuationPolicy.Evaluate(
        target,
        "player-1",
        100,
        npcAvailable: true);
    Assert(allowed == ConversationContinuationBlockReason.None, "A valid same-NPC continuation was blocked.");
    Assert(
        ConversationContinuationPolicy.Evaluate(target, "player-2", 100, true)
        == ConversationContinuationBlockReason.PlayerChanged,
        "A continuation leaked to another player.");
    Assert(
        ConversationContinuationPolicy.Evaluate(target, "player-1", 101, true)
        == ConversationContinuationBlockReason.DayChanged,
        "A continuation crossed into another day.");
    Assert(
        ConversationContinuationPolicy.Evaluate(target, "player-1", 100, false)
        == ConversationContinuationBlockReason.NpcUnavailable,
        "A continuation opened after the NPC left.");
    Assert(
        ConversationContinuationPolicy.Evaluate(target, "player-1", 100, true)
        == ConversationContinuationBlockReason.None,
        "A continuation was blocked after the player changed maps.");
}

static GiftPolicyContext CreateSchemaV2GiftContext(
    string npcName,
    int heartLevel,
    string deliveryMode,
    IReadOnlyCollection<NpcGiftHistoryEntry>? recentGifts = null)
{
    return new GiftPolicyContext
    {
        ActionId = $"schema-v2-{npcName}-{deliveryMode}-{heartLevel}",
        NpcName = npcName,
        CurrentDay = 100,
        RelevantTags = new[] { "general" },
        HeartLevel = heartLevel,
        DeliveryMode = deliveryMode,
        RecentGifts = recentGifts ?? Array.Empty<NpcGiftHistoryEntry>(),
    };
}

static SocialGiftItemTemplate CreateGiftTemplate(
    string key,
    string itemId,
    string category,
    int priority = 0)
{
    return new SocialGiftItemTemplate
    {
        Key = key,
        QualifiedItemId = itemId,
        DisplayHint = key,
        ApplicableTags = new List<string> { "general" },
        Category = category,
        Priority = priority,
        RepeatCooldownDays = 7,
    };
}

static SocialGiftPoolEntry CreateGiftEntry(string key, string itemId)
{
    return new SocialGiftPoolEntry
    {
        Key = key,
        QualifiedItemId = itemId,
        DisplayHint = key,
        ApplicableTags = new List<string> { "general" },
    };
}

static SocialGiftItemFacts CreateGiftFacts(
    string itemId,
    string displayName,
    int sellPrice,
    bool isTool = false,
    bool isQuestOrUnique = false,
    bool isObject = true)
{
    return new SocialGiftItemFacts
    {
        Exists = true,
        QualifiedItemId = itemId,
        DisplayName = displayName,
        TypeDefinitionId = isObject ? "(O)" : "(T)",
        SellPrice = sellPrice,
        PurchasePrice = sellPrice,
        IsObject = isObject,
        IsTool = isTool,
        IsQuestOrUnique = isQuestOrUnique,
        CanBeTrashed = true,
        CanBeShipped = true,
        CanBeGivenAsGift = true,
    };
}

static GiftPolicyContext CreateGiftContext(
    bool giftAlreadyOfferedToday = false,
    IReadOnlyCollection<string>? completedActionIds = null)
{
    return new GiftPolicyContext
    {
        ActionId = "action-1",
        NpcName = "Abigail",
        CurrentDay = 100,
        GiftAlreadyOfferedToday = giftAlreadyOfferedToday,
        CompletedActionIds = completedActionIds ?? Array.Empty<string>(),
        RelevantTags = new[] { "mining" },
    };
}

static async Task TestLiveStreamingAsync()
{
    string apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? string.Empty;
    Assert(apiKey.Length > 0, "DEEPSEEK_API_KEY is required for --live.");

    using var httpClient = new HttpClient
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    AiRuntimeProfile profile = CreateAiProfile(
        AiProviderNames.DeepSeek,
        "https://api.deepseek.com",
        "deepseek-v4-flash",
        apiKey) with
    {
        RequestTimeout = TimeSpan.FromSeconds(60),
    };
    var client = new AiProviderClient(httpClient, () => profile);
    var chunks = new List<DeepSeekStreamChunk>();
    string reply = await client.StreamChatAsync(
        apiKey,
        new DeepSeekChatRequest
        {
            Model = "deepseek-v4-flash",
            Thinking = new DeepSeekThinkingOptions { Type = "disabled" },
            ReasoningEffort = "low",
            MaxTokens = 128,
            Stream = true,
            Messages = new List<DeepSeekChatMessage>
            {
                new("system", "You are a connectivity test."),
                new("user", "Reply with exactly STREAM_OK."),
            },
        },
        chunks.Add);

    Assert(chunks.Count > 0, "Live stream returned no chunks.");
    Assert(reply.Contains("STREAM_OK", StringComparison.Ordinal), "Unexpected live streaming response.");
    Console.WriteLine($"Live DeepSeek stream passed with {chunks.Count} chunks: {reply}");
}

static NpcConversationMemory CreateMemory(int messageCount)
{
    var memory = new NpcConversationMemory
    {
        PlayerId = "1",
        NpcName = "Abigail",
    };

    for (int index = 0; index < messageCount; index++)
    {
        memory.Messages.Add(new ConversationMemoryMessage
        {
            Role = index % 2 == 0 ? "user" : "assistant",
            Content = $"message-{index}",
            GameDate = $"Y1 spring {index + 1}",
        });
    }

    return memory;
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

sealed record CapturedHttpRequest(Uri Uri, string Authorization, string Body);

sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpResponseMessage> responseFactory;

    public RecordingHttpHandler(Func<HttpResponseMessage> responseFactory)
    {
        this.responseFactory = responseFactory;
    }

    public List<CapturedHttpRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedHttpRequest(
            request.RequestUri ?? throw new InvalidOperationException("Request URI is missing."),
            request.Headers.Authorization?.ToString() ?? string.Empty,
            body));
        return responseFactory();
    }
}

sealed class RecordingClient : IDeepSeekClient
{
    private readonly Queue<object> responses;

    public RecordingClient(params object[] responses)
    {
        this.responses = new Queue<object>(responses);
    }

    public List<DeepSeekChatRequest> Requests { get; } = new();

    public Task<string> CompleteChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        object response = responses.Dequeue();
        return response is Exception exception
            ? Task.FromException<string>(exception)
            : Task.FromResult((string)response);
    }

    public Task<string> StreamChatAsync(
        string apiKey,
        DeepSeekChatRequest request,
        Action<DeepSeekStreamChunk> onChunk,
        CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        object response = responses.Dequeue();
        if (response is Exception exception)
            return Task.FromException<string>(exception);

        string reply = (string)response;
        int split = reply.Length / 2;
        onChunk(new DeepSeekStreamChunk(string.Empty, "reasoning"));
        onChunk(new DeepSeekStreamChunk(reply[..split], string.Empty));
        onChunk(new DeepSeekStreamChunk(reply[split..], string.Empty));
        return Task.FromResult(reply);
    }
}

sealed class FakeGiftItemResolver : ISocialGiftItemResolver
{
    private readonly IReadOnlyDictionary<string, SocialGiftItemFacts> factsByItemId;

    public FakeGiftItemResolver(IReadOnlyDictionary<string, SocialGiftItemFacts> factsByItemId)
    {
        this.factsByItemId = factsByItemId;
    }

    public bool TryResolve(string qualifiedItemId, out SocialGiftItemFacts? facts)
    {
        bool found = factsByItemId.TryGetValue(qualifiedItemId, out SocialGiftItemFacts? value);
        facts = value;
        return found;
    }
}
