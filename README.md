# Vivant Valley

## 中文

Vivant Valley 不只是给每个村民套上聊天窗口，而是让每一次相遇都成为会继续生长的故事。NPC 会带着自己的性格、关系和生活轨迹面对你：他们可能答应邀请，也可能因为当下的心情和立场拒绝；他们会记住共同经历，并在下一次见面时用自己的方式提起它。

对话会结合日期、天气、地点、任务、玩家近期活动、原版剧情和实时场景快照。真正的游戏行动由 LangGraph 工具和 C# 游戏桥接执行，结果会回到对话中，因此“我们到了”“鱼上钩了”或“我受伤了”都有真实游戏状态作为依据。

正式发布包内置平台对应的 LangGraph 后端，安装后会自动在本机回环地址启动；玩家不需要安装 Python、pip 或虚拟环境。

当前版本：`0.13.0`

## English

Vivant Valley is more than a chat window attached to every villager. It turns each encounter into a story that can continue. NPCs meet you with their own personalities, relationships, and daily lives: they may accept an invitation or refuse it based on their mood and motives, remember what you experienced together, and bring it up in their own way later.

Dialogue is grounded in the date, weather, location, quests, recent player activity, vanilla story facts, and a live scene snapshot. Actual game actions are executed by LangGraph tools through a C# game bridge, and the results return to the conversation. Claims such as arriving, catching a fish, or being injured are backed by real game state.

Release packages include a platform-specific LangGraph backend that starts automatically on the local loopback address. Players do not need Python, pip, or a virtual environment.

Current version: `0.13.0`

## 核心功能 / Core Features

### 中文

- **角色化 AI 对话**：靠近并面向村民，按 `Space` 开始对话。模型会参考星露谷原版角色的性格、语气、价值观和经历。
- **现场上下文**：日期、天气、地点、任务、玩家进度、近期活动、原版剧情事实和 NPC 身边正在发生的事都会进入本轮情境。
- **共同记忆**：保留近期聊天、临时同行经历和压缩后的长期摘要；记忆不会覆盖实时事实或角色性格。
- **真实工具执行**：AI 只提出行动意图，LangGraph ToolNode 和 C# 游戏桥接执行并回传结果。礼物、移动、钓鱼和战斗不会停留在台词里。
- **同行旅行**：NPC 接受邀请后由玩家带路，跟随玩家前往目标地点并处理地图、房屋和矿洞切换；活动结束后恢复日程，没有日程时会回家。
- **矿洞护卫**：NPC 使用默认银河剑，主动寻找附近怪物并作战。NPC 有生命条；被击败会损失半颗心、住院一天，并记住这次经历。
- **钓鱼伙伴**：玩家到达钓鱼点并抛竿后，NPC 使用铱金鱼竿完成抛竿、等待、收杆和捕获，真实鱼获会交给玩家。
- **有分寸的社交与礼物**：NPC 会根据性格、关系和情境决定是否接受请求。礼物来自代码控制的合法候选池，支持当面交付和次日惊喜邮件。
- **主动相遇**：社交导演依据原版日程、近期互动和关系安排自然搭话；合适时 NPC 也可能带着礼物或邮件来找你。
- **可配置体验**：支持 DeepSeek、OpenAI、兼容 Base URL 和自定义模型；可分别调整对话窗口与主动对话窗口大小。

### English

- **Personality-driven dialogue**: Face a nearby villager and press `Space`. Responses follow the character's original Stardew Valley personality, voice, values, and history.
- **Live context**: Date, weather, location, quests, player progress, recent activity, vanilla story facts, and nearby events can inform each turn.
- **Shared memory**: Recent dialogue, temporary travel facts, and bounded long-term summaries preserve continuity without overriding live facts or personality.
- **Real tool execution**: The AI proposes an intent; LangGraph ToolNodes and the C# game bridge execute and report the authoritative result. Gifts, travel, fishing, and combat are real game actions.
- **Shared travel**: After accepting an invitation, an NPC follows the player through map, house, and mine transitions, then resumes their schedule. NPCs without a usable schedule go home.
- **Mine guard**: The NPC uses a default Galaxy Sword and actively fights nearby monsters. NPCs have health; defeat costs half a heart, sends them to the hospital for one day, and becomes a remembered event.
- **Fishing companion**: After the player reaches a fishing spot and casts, the NPC uses an Iridium Rod for casting, waiting, reeling, and catching. Real fish are delivered to the player.
- **Consent-based gifts and actions**: NPCs decide whether to accept requests based on personality, relationship, and context. Gifts come from a code-controlled valid pool and may arrive face-to-face or by surprise mail.
- **Proactive encounters**: The social director uses vanilla schedules, recent interactions, and relationships to arrange natural conversations, gifts, and letters.
- **Configurable experience**: Supports DeepSeek, OpenAI, compatible Base URLs, custom models, and separate window scaling.

## 运行要求 / Requirements

### 中文

- Stardew Valley 1.6 或更高版本
- SMAPI 4.0 或更高版本
- DeepSeek 或 OpenAI API Key
- 可以访问所选 AI 提供商的网络环境

Vivant Valley 不捆绑 AI 模型。API 调用可能产生由对应提供商收取的费用。

### English

- Stardew Valley 1.6 or later
- SMAPI 4.0 or later
- A DeepSeek or OpenAI API key
- Internet access to the selected AI provider

Vivant Valley does not bundle an AI model. API usage may incur fees charged by the selected provider.

## 安装 / Installation

### 中文

1. 安装 Stardew Valley 1.6 和 SMAPI 4.x。
2. 下载适合自己系统的发布包；Windows 使用 `VivantValley-Windows-x64-Release.zip`，macOS 使用对应的 Intel 或 Apple Silicon 安装包。
3. 将压缩包内的 `VivantValley` 文件夹解压到游戏的 `Mods` 目录。
4. 确认最终结构为 `Mods/VivantValley/manifest.json`，不要多套一层文件夹。
5. 通过 SMAPI 启动游戏，载入存档后完成 AI 提供商设置。

### English

1. Install Stardew Valley 1.6 and SMAPI 4.x.
2. Download the package for your system: Windows uses `VivantValley-Windows-x64-Release.zip`; macOS uses the matching Intel or Apple Silicon package.
3. Extract the included `VivantValley` folder into the game's `Mods` directory.
4. Verify that the final path is `Mods/VivantValley/manifest.json`; do not add an extra directory layer.
5. Launch through SMAPI, load a save, and complete the AI provider setup.

### 从 Stardew AI Memories 升级 / Upgrading

`Vivant Valley 0.13.0` 是原项目的正式改名版本。SMAPI `UniqueID` 继续使用 `firstmod.StardewAIMemories`，已有聊天记忆、社交计划和邮件状态保持兼容。

`Vivant Valley 0.13.0` is the official renamed version of the original project. The SMAPI `UniqueID` remains `firstmod.StardewAIMemories`, so existing conversation memories, social plans, and mail state remain compatible.

升级时请关闭游戏，备份旧 `config.json`，移除旧的 `Mods/StardewAIMemories` 文件夹，安装新的 `Mods/VivantValley`，再将旧配置复制到新目录。不要同时保留新旧两个安装目录。

When upgrading, close the game, back up the old `config.json`, remove the old `Mods/StardewAIMemories` folder, install `Mods/VivantValley`, and copy the old configuration into the new folder. Do not keep both installation folders.

## 初次配置 / First-Time Setup

### 中文

载入存档后，打开 Vivant Valley 设置界面并选择提供商：

- DeepSeek：默认 Base URL 为 `https://api.deepseek.com`。
- OpenAI：默认 Base URL 为 `https://api.openai.com/v1`。
- Base URL 填写服务根地址；客户端会自动规范化并追加聊天完成端点。
- 模型名称必须是当前 API Key 有权访问的模型。

也可以使用环境变量 `DEEPSEEK_API_KEY` 或 `OPENAI_API_KEY`。游戏内保存的提供商 Key 优先于环境变量。

### English

After loading a save, open Vivant Valley's settings menu and choose a provider:

- DeepSeek: default Base URL is `https://api.deepseek.com`.
- OpenAI: default Base URL is `https://api.openai.com/v1`.
- Enter the service root as the Base URL; the client normalizes it and appends the chat-completion endpoint.
- Use a model name that the configured API key can access.

Keys can also be supplied through `DEEPSEEK_API_KEY` or `OPENAI_API_KEY`. A provider key saved in-game takes precedence.

## 使用方法 / Usage

| 操作 / Action | 默认方式 / Default |
| --- | --- |
| 开始对话 / Start | 靠近并面向 NPC，按 `Space` / Face a nearby NPC and press `Space` |
| 继续 / Continue | 回复结束后按 `Enter` 或点击“继续” / Press `Enter` or click Continue |
| 关闭 / Close | 按 `Esc` 或点击“关闭” / Press `Esc` or click Close |
| AI 设置 / AI settings | 设置按钮或运行 `vivant_settings` / Settings button or `vivant_settings` |
| 查看状态 / Status | `vivant_status` |
| 清除记忆 / Forget | `vivant_forget <NPC内部名\|all>` |
| 社交计划 / Social plan | `vivant_social_status [NPC内部名]` |

旧命令 `aimemory_key`、`aimemory_settings`、`aimemory_status`、`aimemory_forget` 和 `aisocial_status` 仍可使用。 / Legacy commands remain available for compatibility.

## 距离与地图规则 / Distance and Map Rules

### 中文

- 开始普通对话时，玩家和 NPC 必须在同一地图、默认 `3.5` 格内。
- 对话框打开后，走远、NPC 移动或换地图不会中断当前对话。
- 主动相遇只在 NPC 按原版日程与玩家同地图且进入默认 `7` 格范围时触发。
- 同行需要 NPC 真实接受；玩家带路，NPC 在约一格距离内跟随，并延迟进入地图、房屋或矿洞，避免挡路。
- 同行活动结束后 NPC 恢复原版日程，没有日程时返回家中。

### English

- To start a normal conversation, the player and NPC must be on the same map and within the default `3.5`-tile range.
- Once open, walking away, NPC movement, or changing maps does not interrupt the active conversation.
- Proactive encounters trigger only when the NPC is on the same map under their vanilla schedule and enters the default `7`-tile range.
- Shared activities require actual NPC acceptance. The player leads, the NPC follows at roughly one tile, and transitions are delayed so the player is not blocked.
- After the activity ends, the NPC resumes their vanilla schedule or goes home if no usable schedule exists.

## 对话、记忆与剧情事实 / Dialogue, Memory, and Story Facts

### 中文

每次对话会组合当前游戏事实、NPC 原版人格提示、近期聊天、长期摘要、原版剧情记录和实时场景快照。当前存档事实优先于旧记忆，提示词禁止提前泄露尚未发生的事件或任务结局。最近 8 轮的真实共同经历只保留在临时会话中，跨天或重新载入存档后清除。AI 对话不替代原版每日交谈，也不会直接增加好感度；真实礼物、同行、钓鱼和矿洞结果会记录为剧情档案。

### English

Each conversation combines current game facts, the NPC's vanilla personality prompt, recent chat, long-term summaries, vanilla story records, and a live scene snapshot. Current save facts take priority over old memories, and the prompt forbids revealing future events or quest outcomes. Real shared experiences from the latest eight turns are temporary session facts and are cleared across days or after reloading. AI dialogue does not replace vanilla daily dialogue or directly add friendship; real gift, travel, fishing, and mine outcomes become story context.

## 主动社交 / Proactive Social

### 中文

社交导演根据近期积极对话、温暖度、关心度、未完话题、关系和相遇多样性选择候选。键鼠模式每天 `3-5` 名；手柄模式最多 `6` 名；每名候选有早上和下午两个机会。玩家活动只按天保存地点类别、物品类别、技能变化和活跃时段等摘要，默认保留 7 天。

### English

The social director selects candidates using recent positive dialogue, warmth, care, open topics, relationships, and encounter variety. Keyboard and mouse mode uses `3-5` candidates per day; controller mode can use up to `6`; each candidate has morning and afternoon opportunities. Player activity is retained only as a seven-day daily summary of location categories, item categories, skill changes, and active periods.

## 礼物规则 / Gift Rules

### 中文

礼物目录位于 `assets/social/gift-pools.json`。AI 只能看到代码预先筛选的候选 key，不能提交任意物品 ID。代码会拒绝无效候选、未达到红心要求的候选、工具/武器/任务物品、重复行动和冷却中的礼物。`give_gift` 会先完成真实交付，再生成可见回复；背包已满时物品会落在玩家脚边。隔夜邮件每天最多安排 `0-2` 封，并使用原版可点击邮件机制。

### English

The gift catalog is `assets/social/gift-pools.json`. The AI sees only code-filtered candidate keys and cannot submit arbitrary item IDs. Code rejects invalid candidates, insufficient-heart gifts, tools, weapons, quest items, duplicate actions, and gifts on cooldown. `give_gift` completes real delivery before the visible reply; a full inventory drops the item near the player. Overnight planning may select `0-2` surprise mails using the vanilla clickable mail system.

## 主要配置 / Main Configuration

SMAPI 首次运行后会在模组目录生成 `config.json`。/ SMAPI creates `config.json` in the Mod folder on first run.

| 配置项 / Setting | 默认值 / Default | 说明 / Description |
| --- | ---: | --- |
| `ChatKey` | `Space` | 普通对话快捷键 / Normal conversation key |
| `MaxTalkDistanceTiles` | `3.5` | 普通对话距离 / Conversation range |
| `EnableThinking` | `false` | 模型思考模式 / Model thinking mode |
| `ReasoningEffort` | `low` | 推理强度 / Reasoning effort |
| `MaxContextMessages` | `24` | 每次请求的近期消息上限 / Recent messages per request |
| `SummaryTriggerMessages` | `24` | 触发长期摘要的消息数 / Summary trigger count |
| `SummaryKeepRecentMessages` | `8` | 摘要后保留的消息数 / Messages kept after summary |
| `EnableSocialDirector` | `true` | 每日主动社交 / Daily proactive social |
| `DailyCandidateMin` / `DailyCandidateMax` | `3` / `5` | 键鼠候选；手柄最多 6 名 / Keyboard candidates; up to 6 with controller |
| `SocialActivationDistanceTiles` | `7` | 主动相遇距离 / Proactive encounter range |
| `EnableConversationSignalAnalysis` | `true` | 对话信号提取 / Conversation signal extraction |
| `EnableOvernightMailGifts` | `true` | 隔夜惊喜邮件 / Overnight surprise mail |
| `MaxOvernightMailGifts` | `2` | 每日邮件上限 / Daily mail limit |

旧版字段仅为兼容已有配置保留，当前流程不会使用。/ Legacy fields are retained only for configuration compatibility and are not used by the current flow.

## 数据与隐私 / Data and Privacy

发送给 AI 提供商的内容可能包括玩家输入、近期对话、长期摘要、有限活动摘要，以及当前日期、地点、天气、关系、任务和已发生剧情事实。API Key 通过游戏内设置保存时会以明文写入模组目录的 `config.json`，不会进入存档、日志或发布包。不要分享 `config.json`。

Content sent to the selected AI provider may include player input, recent dialogue, long-term summaries, limited activity summaries, and current date, location, weather, relationship, quests, and completed story facts. An API key saved in-game is written in plain text to `config.json` in the Mod folder and is not stored in saves, logs, or release packages. Do not share `config.json`.

## 联机说明 / Multiplayer

普通 AI 对话支持本地分屏和远程农场助手，请求状态按屏幕隔离。长期持久化、每日社交规划、主动相遇、礼物和隔夜邮件目前只由主玩家执行；农场助手的普通聊天记忆只保留到本次连接结束。

Normal AI dialogue supports local split-screen and remote farmhands with per-screen request state. Long-term persistence, daily social planning, proactive encounters, gifts, and overnight mail currently run only for the main player. Farmhand conversation memory lasts only for the current connection.

## 开发与构建 / Development and Build

项目默认引用路径为 `E:\SteamLibrary\steamapps\common\Stardew Valley`。/ The default game reference path is `E:\SteamLibrary\steamapps\common\Stardew Valley`.

Windows Release 包 / Windows Release package:

```powershell
.\scripts\package.ps1 -Configuration Release -BackendPlatform win-x64
```

如果游戏安装在其他位置 / If the game is installed elsewhere:

```powershell
.\scripts\package.ps1 -Configuration Release -GamePath "D:\Games\Stardew Valley" -BackendPlatform win-x64
```

macOS 后端应在对应架构的 Mac 上构建：Intel 使用 `osx-x64`，Apple Silicon 使用 `osx-arm64`。/ Build macOS backends on the matching Mac architecture: `osx-x64` for Intel and `osx-arm64` for Apple Silicon.

手动构建 Mod / Build the Mod manually:

```powershell
dotnet build .\VivantValley.csproj -c Release
```

冒烟测试 / Smoke test:

```powershell
dotnet run --project .\tests\ConversationEngineSmoke\ConversationEngineSmoke.csproj -c Release
```

发布输出位于 `dist/VivantValley/` 和平台对应的 ZIP 文件中。/ Release output is placed in `dist/VivantValley/` and the platform-specific ZIP file.

## 兼容身份 / Compatibility Identity

虽然产品名称和 DLL 已改为 Vivant Valley，SMAPI `UniqueID` 仍为 `firstmod.StardewAIMemories`。该值是已有存档数据和升级识别的一部分，不应在后续版本中修改。

Although the product name and DLL are now Vivant Valley, the SMAPI `UniqueID` remains `firstmod.StardewAIMemories`. It is part of existing save data and upgrade detection and should not be changed in future versions.
