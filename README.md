# Vivant Valley

Vivant Valley 想做的，不是给每个村民套上一个聊天窗口，而是让每一次相遇都成为一段会继续生长的故事。NPC 会带着自己的性格、关系和生活轨迹面对你：他们可能答应你的邀请，也可能因为当下的心情和立场拒绝；他们会记住你们共同经历过的事，也会在下一次见面时用自己的方式提起它。

对话会结合日期、天气、地点、任务、玩家近期活动、原版剧情和实时场景快照。真正的游戏行动由 LangGraph 工具调用和 C# 游戏桥接执行，执行结果会回到对话中，因此 NPC 说“我们到了”“鱼已经上钩”或“我受伤了”时，背后都有真实的游戏状态作为依据。

正式发布包内置平台对应的 LangGraph 后端，安装 Mod 后会自动在本机回环地址启动；玩家不需要安装 Python、pip 或虚拟环境。

当前版本：`0.13.0`

## 核心功能

- **角色化 AI 对话**：靠近并面向村民，按 `Space` 开始一场不会被通用模板抹平的对话。模型会严格参考星露谷原版角色的性格、语气、价值观和经历。
- **有现场感的上下文**：日期、天气、地点、任务、玩家进度、近期活动、原版剧情事实，以及 NPC 身边正在发生的事都会进入本轮情境。
- **会延续的共同记忆**：保留近期聊天、临时同行经历和经过压缩的长期摘要；记忆用于让关系有连续性，但不会覆盖实时游戏事实或角色性格。
- **真正执行的游戏行动**：AI 只提出行动意图，实际状态由 LangGraph 的工具节点和 C# 游戏桥接完成并回传。礼物、移动、钓鱼和战斗不会停留在台词里。
- **同行旅行**：NPC 接受邀请后由玩家带路，跟随玩家前往目标地点，处理地图和门的切换；同行结束后 NPC 会恢复自己的日程，没有日程时会回家。
- **矿洞护卫**：邀请 NPC 一起下矿后，NPC 会装备默认的银河剑，主动寻找附近的怪物并挥剑作战。NPC 有自己的生命条；被击败会损失半颗心、住院一天，并记住这次经历。
- **钓鱼伙伴**：玩家到达钓鱼点并抛竿后，NPC 会拿起铱金鱼竿完成抛竿、等待、收杆和捕获动作，钓到的真实鱼会交给玩家。
- **有分寸的社交与礼物**：NPC 会根据性格、关系和情境决定是否接受请求。礼物来自代码控制的合法候选池，支持当面交付和次日惊喜邮件，AI 不能生成任意物品或伪造结果。
- **主动相遇**：社交导演会依据原版日程、近期互动和关系安排自然的主动搭话；合适时 NPC 也可能带着礼物或一封邮件来找你。
- **可配置的体验**：支持 DeepSeek、OpenAI 及兼容的 Base URL 和自定义模型；游戏内可调整 AI 设置、对话窗口和主动对话窗口大小。

## 运行要求

- Stardew Valley 1.6
- SMAPI 4.0 或更高版本
- DeepSeek 或 OpenAI API Key
- 可访问所选 AI 提供商的网络环境

Vivant Valley 不捆绑模型服务。API 调用可能产生由对应提供商收取的费用。

## 安装

1. 安装 Stardew Valley 1.6 和 SMAPI 4.x。
2. 下载适合自己系统的发布包；Windows 使用 `VivantValley-Windows-x64-Release.zip`。macOS 使用单独发布的 Intel 或 Apple Silicon 安装包。
3. 将压缩包内的 `VivantValley` 文件夹解压到游戏的 `Mods` 目录。
4. 确认最终结构为 `Mods/VivantValley/manifest.json`，不要多套一层文件夹。
5. 通过 SMAPI 启动游戏，载入存档后完成 AI 提供商设置。

### 从 Stardew AI Memories 升级

`Vivant Valley 0.13.0` 是原项目的正式改名版本。SMAPI `UniqueID` 继续使用 `firstmod.StardewAIMemories`，因此已有聊天记忆、社交计划和邮件状态保持兼容。

升级时请关闭游戏，然后：

1. 备份旧目录中的 `config.json`。
2. 删除或移出旧的 `Mods/StardewAIMemories` 文件夹，避免同一 `UniqueID` 被加载两次。
3. 安装新的 `Mods/VivantValley` 文件夹。
4. 将原 `config.json` 放入新目录。

不要同时保留新旧两个安装目录。

## 初次配置

载入存档后，打开 Vivant Valley 的设置界面并选择提供商：

- DeepSeek：默认 Base URL 为 `https://api.deepseek.com`。
- OpenAI：默认 Base URL 为 `https://api.openai.com/v1`。
- Base URL 应填写服务根地址；客户端会自动规范化并追加聊天完成端点。
- 模型名称由用户填写，必须是当前 API Key 有权访问的模型。

也可以通过环境变量提供 Key：

- `DEEPSEEK_API_KEY`
- `OPENAI_API_KEY`

游戏内保存的对应提供商 Key 优先于环境变量。

## 使用方法

| 操作 | 默认方式 |
| --- | --- |
| 开始对话 | 靠近并面向 NPC，按 `Space` |
| 继续对话 | 回复结束后按 `Enter` 或点击“继续” |
| 关闭对话 | 按 `Esc` 或点击“关闭” |
| 打开 AI 设置 | 对话输入界面的“设置”按钮，或运行 `vivant_settings` |
| 查看状态 | `vivant_status` |
| 清除记忆 | `vivant_forget <NPC内部名\|all>` |
| 查看社交计划 | `vivant_social_status [NPC内部名]` |

旧命令 `aimemory_key`、`aimemory_settings`、`aimemory_status`、`aimemory_forget` 和 `aisocial_status` 仍可使用，以兼容旧版本。

### 距离与地图规则

- 开始普通对话时，玩家必须与可交谈 NPC 位于同一地图，并处于默认 `3.5` 格的选择距离内。
- 对话框成功打开后，普通对话不会因为玩家走远、NPC 移动或玩家换地图而中断。
- 对话已经开始后，当面礼物不会再次检查距离或地图；交付失败只影响礼物，不会中断对话。
- 主动相遇只会在 NPC 按原版日程与玩家同地图且进入默认 `7` 格范围时触发。
- 同行行动开始前需要 NPC 的真实接受结果。接受后由玩家带路，NPC 会在约一格距离内跟随，并在地图、房屋或矿洞切换时延迟进入，避免挡住玩家。
- 同行到达目的地并结束活动后，NPC 会回到原版日程；没有可用日程时会返回家中。普通聊天和主动相遇不会强制 NPC 离开自己的日程。

## 对话、记忆与剧情事实

每次普通对话会组合当前游戏事实、NPC 原版人格提示、近期聊天、长期摘要、部分原版剧情记录和实时场景快照。提示词会明确要求模型成为当前《星露谷物语》NPC，并严格遵循该角色的原版性格、说话方式、价值观和生活背景；不再依赖逐人物外部人格配置。当前存档事实优先于旧记忆，提示词禁止提前泄露尚未发生的事件或任务结局。

当前游戏会话还会单独保留最近 8 轮内的少量真实共同经历，例如 NPC 正在陪玩家前往某地或今天已经一起到达过某地。这类临时事实不写入长期人格记忆，跨天或重新载入存档后清除。

AI 对话是独立聊天渠道，不替代原版每日交谈，也不会直接增加好感度。原版事件中的可见对白、玩家选择、礼物结果，以及同行、钓鱼和矿洞护卫的真实结果会被整理为剧情档案，供后续对话参考。

## 主动社交

每天首次载入后，社交导演会根据近期对话的积极程度、温暖度、关心度、未完话题、关系和相遇多样性选择候选。键鼠模式沿用 `3-5` 名；检测到游戏手柄模式时提高到最多 `6` 名。每名候选有早上与下午两个独立机会，错过时段后不会追赶或顺延。

玩家活动只保存按天汇总的地点类别、物品类别、技能变化和活跃时段等有限信息，不记录坐标、物品 ID 或完整事件流。默认保留 7 天。

## 礼物规则

礼物目录位于 `assets/social/gift-pools.json`。AI 只能看到代码预先筛选的候选 key，不能提交任意物品 ID。

代码会拒绝：

- 不存在或不属于当前 NPC 的候选；
- 未达到最低红心要求的候选；
- 工具、武器、任务物品、唯一物品或不可正常赠送的物品；
- 重复行动、同一 NPC 当天的第二次礼物提议；
- 仍处于该物品重复冷却期的候选。

`signature`、`activity`、`seasonal` 和 `care` 分类统一使用 3 天重复冷却；`general` 与 `fallback` 使用礼物目录中配置的冷却。单价低于 100g 的小礼物会堆叠至约 100g，单价达到 100g 时只送 1 个。

普通聊天中的 `give_gift` 会先完成真实交付，再生成玩家可见回复。若背包已满，物品会落在玩家脚边。任何校验或交付失败都只取消礼物，NPC 会继续正常对话。

隔夜邮件由独立规划器分析当天完成的普通 AI 对话，并从合适的 NPC 中选择 `0-2` 封次日惊喜邮件。附件使用原版可点击邮件机制，领取结果会以幂等状态保存。

## 主要配置

SMAPI 首次运行后会在模组目录生成 `config.json`。

| 配置项 | 默认值 | 说明 |
| --- | ---: | --- |
| `ChatKey` | `Space` | 普通 AI 对话快捷键 |
| `MaxTalkDistanceTiles` | `3.5` | 开始普通对话的最大选择距离 |
| `EnableThinking` | `false` | 是否启用模型思考模式 |
| `ReasoningEffort` | `low` | 提供商支持时发送的推理强度 |
| `MaxContextMessages` | `24` | 每次请求携带的近期消息上限 |
| `SummaryTriggerMessages` | `24` | 触发长期摘要的消息数量 |
| `SummaryKeepRecentMessages` | `8` | 摘要后保留的近期消息数量 |
| `EnableSocialDirector` | `true` | 是否启用每日主动社交 |
| `DailyCandidateMin` / `DailyCandidateMax` | `3` / `5` | 键鼠模式每日社交候选数量；手柄模式固定尝试选择 6 名 |
| `SocialActivationDistanceTiles` | `7` | 主动相遇触发距离 |
| `EnableConversationSignalAnalysis` | `true` | 是否提取有限的对话社交信号 |
| `EnableOvernightMailGifts` | `true` | 是否启用隔夜惊喜邮件 |
| `MaxOvernightMailGifts` | `2` | 每天最多安排的惊喜邮件数量 |

旧版 `DailyEncounterLimit`、`NpcProactiveCooldownDays`、`NpcGiftCooldownDays`、`DailyGiftLimit`、`SocialGiftMaximumValue` 和 `Proactive*` 字段仅为兼容已有配置保留，当前流程不会使用它们。

## 数据与隐私

发送给所选 AI 提供商的内容可能包括：

- 玩家本轮输入；
- 近期对话与长期记忆摘要；
- 有限的活动和社交信号摘要；
- 当前日期、地点、天气、关系、任务与部分已发生剧情事实。

API Key 若通过游戏内设置保存，会以明文写入模组目录的 `config.json`。它不会进入存档、日志或发布压缩包。不要分享自己的 `config.json`。

## 联机说明

普通 AI 对话支持本地分屏和远程农场助手，请求状态按屏幕隔离。长期持久化、每日社交规划、主动相遇、礼物和隔夜邮件目前只由主玩家执行；农场助手的普通聊天记忆只保留到本次连接结束。

## 开发与构建

项目默认引用路径为 `E:\SteamLibrary\steamapps\common\Stardew Valley`。生成 Release 包：

```powershell
.\scripts\package.ps1 -Configuration Release
```

如果游戏安装在其他位置：

```powershell
.\scripts\package.ps1 -Configuration Release -GamePath "D:\Games\Stardew Valley"
```

手动构建：

```powershell
dotnet build .\VivantValley.csproj -c Release
```

运行无需 API Key 的冒烟测试：

```powershell
dotnet run --project .\tests\ConversationEngineSmoke\ConversationEngineSmoke.csproj -c Release
```

发布输出位于：

- `dist/VivantValley/`
- `dist/VivantValley-Release.zip`

## 兼容身份

虽然产品名称和 DLL 已改为 Vivant Valley，SMAPI `UniqueID` 仍为 `firstmod.StardewAIMemories`。该值是已有存档数据和升级识别的一部分，不应在后续版本中修改。
