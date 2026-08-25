using StardewModdingAPI;

namespace VivantValley;

/// <summary>Player-editable settings stored in config.json.</summary>
public sealed class ModConfig
{
    /// <summary>Global AI connection profiles shared by every local save.</summary>
    public AiProviderSettings Ai { get; set; } = new();

    /// <summary>The key used while facing a villager to start an AI conversation.</summary>
    public SButton ChatKey { get; set; } = SButton.Space;

    /// <summary>Whether a villager can be selected if nobody is on the player's grab tile.</summary>
    public bool AllowNearbyNpcFallback { get; set; } = true;

    /// <summary>Maximum distance in tiles for selecting the initial NPC.</summary>
    public float MaxTalkDistanceTiles { get; set; } = 3.5f;

    /// <summary>
    /// Prompt for a session-only key after the first save is loaded each time the game starts.
    /// An already-set DEEPSEEK_API_KEY environment variable satisfies this prompt.
    /// </summary>
    public bool PromptForApiKeyEveryLaunch { get; set; } = true;

    /// <summary>Legacy 0.11 endpoint retained for one-time settings migration.</summary>
    public string ApiUrl { get; set; } = "https://api.deepseek.com/chat/completions";

    /// <summary>Legacy 0.11 model retained for one-time settings migration.</summary>
    public string Model { get; set; } = "deepseek-v4-flash";

    /// <summary>Enable the API's thinking mode.</summary>
    public bool EnableThinking { get; set; } = false;

    /// <summary>Reasoning effort sent to DeepSeek.</summary>
    public string ReasoningEffort { get; set; } = "low";

    /// <summary>HTTP timeout for each API operation.</summary>
    public int RequestTimeoutSeconds { get; set; } = 90;

    /// <summary>Local LangGraph service endpoint used for structured conversation decisions.</summary>
    public string LangGraphBaseUrl { get; set; } = "http://127.0.0.1:8123";

    /// <summary>HTTP timeout for one LangGraph graph request.</summary>
    public int LangGraphTimeoutSeconds { get; set; } = 240;

    /// <summary>Loopback port used by Python ToolNode functions to call SMAPI.</summary>
    public int LangGraphBridgePort { get; set; } = 8124;

    /// <summary>Maximum recent memory messages sent with each request.</summary>
    public int MaxContextMessages { get; set; } = 24;

    /// <summary>Summarize old messages after this many stored messages.</summary>
    public int SummaryTriggerMessages { get; set; } = 24;

    /// <summary>Messages kept verbatim after a successful long-term-memory summary.</summary>
    public int SummaryKeepRecentMessages { get; set; } = 8;

    /// <summary>Maximum number of seen event IDs included in the live game snapshot.</summary>
    public int MaxSeenEventIdsInContext { get; set; } = 12;

    /// <summary>Maximum active quests included in the live game snapshot.</summary>
    public int MaxQuestsInContext { get; set; } = 4;

    /// <summary>Number of newest vanilla story episodes included with their visible lines intact.</summary>
    public int MaxCompleteNarrativeEpisodesInContext { get; set; } = 2;

    /// <summary>Number of older vanilla story episodes represented by stable choices, gifts, and endings.</summary>
    public int MaxNarrativeEpisodeAnchorsInContext { get; set; } = 8;

    /// <summary>Preferred prompt budget for vanilla story context. The newest episode is never cut in half.</summary>
    public int MaxNarrativeContextCharacters { get; set; } = 6000;

    /// <summary>Maximum characters displayed and persisted for one NPC reply.</summary>
    public int MaxReplyCharacters { get; set; } = 1200;

    /// <summary>Maximum output tokens requested from DeepSeek for one API operation.</summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>Display scale for the manual conversation composer and reply window.</summary>
    public float ConversationUiScale { get; set; } = 1f;

    /// <summary>Independent display scale for proactive encounter windows.</summary>
    public float ProactiveUiScale { get; set; } = 1f;

    /// <summary>Whether recent positive conversations can produce same-day proactive NPC encounters.</summary>
    public bool EnableSocialDirector { get; set; } = true;

    /// <summary>Minimum number of NPCs sampled for a daily plan when enough NPCs are eligible.</summary>
    public int DailyCandidateMin { get; set; } = 3;

    /// <summary>Maximum number of NPCs sampled for a daily plan.</summary>
    public int DailyCandidateMax { get; set; } = 5;

    /// <summary>Legacy setting retained for existing config files. Every selected morning/evening opportunity is now allowed.</summary>
    public int DailyEncounterLimit { get; set; } = 10;

    /// <summary>How many in-game days a positive AI conversation remains eligible.</summary>
    public int ConversationLookbackDays { get; set; } = 14;

    /// <summary>Minimum bounded conversation valence required for daily candidate selection.</summary>
    public double PositiveConversationThreshold { get; set; } = 0.35;

    /// <summary>Legacy setting retained for existing config files. Cross-day proactive cooldown is disabled.</summary>
    public int NpcProactiveCooldownDays { get; set; }

    /// <summary>Legacy setting retained for existing config files. Cross-day gift cooldown is disabled.</summary>
    public int NpcGiftCooldownDays { get; set; }

    /// <summary>Legacy setting retained for existing config files. Gifts are limited per NPC/day instead.</summary>
    public int DailyGiftLimit { get; set; } = 5;

    /// <summary>Same-location distance required to activate a planned encounter.</summary>
    public float SocialActivationDistanceTiles { get; set; } = 7f;

    /// <summary>Number of bounded daily activity summaries kept in social state.</summary>
    public int ActivityRetentionDays { get; set; } = 7;

    /// <summary>Whether a completed manual chat is classified into a bounded social signal.</summary>
    public bool EnableConversationSignalAnalysis { get; set; } = true;

    /// <summary>Maximum characters shown for one proactive NPC line.</summary>
    public int SocialSceneMaxCharacters { get; set; } = 420;

    /// <summary>Whether completed same-day AI chats can produce surprise mailbox gifts next morning.</summary>
    public bool EnableOvernightMailGifts { get; set; } = true;

    /// <summary>Maximum distinct NPC surprise letters selected from one day's conversations.</summary>
    public int MaxOvernightMailGifts { get; set; } = 2;

    /// <summary>Legacy setting retained for existing config files. Individual gift value is no longer capped.</summary>
    public int SocialGiftMaximumValue { get; set; } = 1000;

    /// <summary>Legacy 0.5 setting retained so existing config files remain readable. It is ignored in 0.6.</summary>
    public bool EnableProactivePilot { get; set; } = true;

    /// <summary>Internal NPC name used by the first proactive story pilot.</summary>
    public string ProactivePilotNpcName { get; set; } = "Abigail";

    /// <summary>Minimum vanilla heart level before a normal chat can schedule a proactive encounter.</summary>
    public int ProactiveMinimumHearts { get; set; } = 2;

    /// <summary>Minimum number of completed manual AI conversations before scheduling an encounter.</summary>
    public int ProactiveMinimumConversationTurns { get; set; } = 1;

    /// <summary>In-game days between the qualifying chat and the earliest encounter.</summary>
    public int ProactiveEncounterDelayDays { get; set; } = 1;

    /// <summary>Number of in-game days for which a planned encounter remains available.</summary>
    public int ProactiveEncounterExpiryDays { get; set; } = 5;

    /// <summary>Minimum in-game days between completed proactive encounters.</summary>
    public int ProactiveEncounterCooldownDays { get; set; } = 5;

    /// <summary>Maximum same-location distance at which the pilot NPC initiates the encounter.</summary>
    public float ProactiveActivationDistanceTiles { get; set; } = 8f;

    /// <summary>Qualified item ID for the pilot gift. The default is one Quartz.</summary>
    public string ProactiveGiftItemId { get; set; } = "(O)80";

    /// <summary>Maximum characters retained from generated proactive-scene dialogue.</summary>
    public int ProactiveSceneMaxCharacters { get; set; } = 420;

    /// <summary>
    /// Legacy 0.11 persistent API key retained for one-time settings migration.
    /// </summary>
    public string ApiKey { get; set; } = "";
}

public static class AiProviderNames
{
    public const string DeepSeek = "DeepSeek";
    public const string OpenAI = "OpenAI";

    public static bool IsSupported(string? value)
        => value?.Trim().Equals(DeepSeek, StringComparison.OrdinalIgnoreCase) == true
           || value?.Trim().Equals(OpenAI, StringComparison.OrdinalIgnoreCase) == true;

    public static string Normalize(string? value)
        => value?.Trim().Equals(OpenAI, StringComparison.OrdinalIgnoreCase) == true
            ? OpenAI
            : DeepSeek;
}

public sealed class AiProviderSettings
{
    public int SchemaVersion { get; set; }

    public string ActiveProvider { get; set; } = AiProviderNames.DeepSeek;

    public AiConnectionProfile DeepSeek { get; set; } = new()
    {
        BaseUrl = "https://api.deepseek.com",
        Model = "deepseek-v4-flash",
    };

    public AiConnectionProfile OpenAI { get; set; } = new()
    {
        BaseUrl = "https://api.openai.com/v1",
        Model = "",
    };

    public AiConnectionProfile GetProfile(string? provider)
        => AiProviderNames.Normalize(provider) == AiProviderNames.OpenAI ? OpenAI : DeepSeek;
}

public sealed class AiConnectionProfile
{
    /// <summary>Provider base URL. The client appends its chat-completions route.</summary>
    public string BaseUrl { get; set; } = "";

    public string Model { get; set; } = "";

    public string ApiKey { get; set; } = "";
}
