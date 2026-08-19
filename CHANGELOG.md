# Changelog

本文件记录 Vivant Valley 的用户可见变更。版本号遵循语义化版本格式。

## [0.13.0] - 2026-08-17

### Changed

- 项目正式更名为 **Vivant Valley**。
- 程序集、项目文件、发布目录和压缩包统一改为 `VivantValley`。
- C# 命名空间统一迁移到 `VivantValley`。
- 新增 `vivant_settings`、`vivant_status`、`vivant_forget` 和 `vivant_social_status` 控制台命令。
- 重写 README，使安装、升级、隐私、AI 配置、距离规则和礼物规则适合公开发布。
- 调整礼物行动规划提示词，降低过度保守措辞，同时继续要求礼物具有上下文意义。

### Compatibility

- 保留 SMAPI `UniqueID`：`firstmod.StardewAIMemories`，已有存档数据继续可读。
- 保留全部 `aimemory_*` 与 `aisocial_status` 旧命令作为兼容别名。
- 保留旧的存档键、动态邮件 ID 前缀和 Harmony 标识，避免升级后重复动作或遗失状态。

## [0.12.0] - 2026-08-16

### Added

- 支持 DeepSeek 与 OpenAI 的游戏内提供商配置、连接测试和热更新。
- 普通对话加入先规划礼物、执行真实结果、再生成最终台词的确定性流程。
- 增加次日惊喜邮件的可恢复规划、内容核验和原版附件领取流程。

### Changed

- 普通对话开启后不再因距离或地图变化中断。
- 当面对话礼物在对话开启后不再重复检查距离或地图。
- 非 `general` / `fallback` 礼物分类的同物品重复冷却统一为 3 天。
- 默认普通对话按键改为 `Space`。

## [0.11.1] - 2026-08-16

### Added

- 新增每日社交导演、早晚主动相遇机会、有限对话信号和七日活动摘要。
- 新增按 NPC 与情境筛选的礼物白名单。

## Earlier Development Builds

`0.2.x` 至 `0.10.x` 为内部迭代版本，逐步加入长期记忆、原版剧情档案、主动场景和礼物策略。公开发布以 `0.13.0` 的行为与文档为准。
