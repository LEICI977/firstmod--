# Vivant Valley 0.13.0 Release

本文档用于发布前核验，不需要放入最终 Mod 压缩包。

## 发布文件

当前 Windows 发布附件名称：

```text
VivantValley-Windows-x64-Release.zip
```

压缩包应只包含以下运行文件：

```text
VivantValley/
  manifest.json
  VivantValley.dll
  VivantValley.pdb
  backend/
    win-x64/
      VivantValley.LangGraph.exe
  assets/
    social/
      gift-pools.json
```

不得包含 `config.json`、API Key、源代码、测试输出、备份目录或旧的 `StardewAIMemories.dll`。

## 发布说明

**Vivant Valley** 把村民之间的一次次相遇写成会延续的故事。NPC 会按照原版性格和自己的意愿回应，并结合日期、天气、地点、任务、近期活动、剧情事实、共同记忆和现场快照说话。接受邀请后，他们可以跟随玩家旅行、进入矿洞担任护卫，或在钓鱼点完成真实的抛竿与收杆；礼物、移动、战斗和捕获结果都由 LangGraph 工具与 C# 游戏桥接实际执行，再回到对话中。NPC 也会在合适的原版日程场景主动搭话、送礼或寄来次日邮件。发布包内置平台后端，玩家不需要安装 Python。

0.13.0 是原 **Stardew AI Memories** 的正式改名版本。现有用户的存档记忆保持兼容，但升级时必须移除旧安装目录，只保留新的 `Mods/VivantValley`，并将旧 `config.json` 复制到新目录。

## 发布前自动检查

```powershell
dotnet build .\VivantValley.csproj -c Release
  dotnet run --project .\tests\ConversationEngineSmoke\ConversationEngineSmoke.csproj -c Release
  .\scripts\build-langgraph-backend.ps1
  .\scripts\package.ps1 -Configuration Release -BackendPlatform win-x64
```

检查压缩包：

- `manifest.json` 的 `Name` 为 `Vivant Valley`。
- 版本号与发布页面一致。
- `EntryDll` 为 `VivantValley.dll`。
- `UniqueID` 仍为 `firstmod.StardewAIMemories`。
- 压缩包中没有 `config.json`。
- 压缩包中没有旧 DLL 或旧项目名目录。

## 手动测试矩阵

- 新安装：首次载入时可以打开 AI 设置并完成连接测试。
- 旧版升级：复制原 `config.json` 后，提供商、模型、Key 和快捷键保持不变。
- 存档兼容：旧 NPC 记忆、社交状态和待领取邮件仍可读取。
- 普通对话：在 3.5 格内按 `Space` 可以开始对话。
- 对话延续：对话框开启后走远或换地图不会中断请求。
- 当面礼物：成功时背包或脚边收到准确物品；失败时对话继续且台词不声称已送礼。
- 同行旅行：NPC 接受邀请后由玩家带路，在同地图、房屋和矿洞切换时保持跟随；到达并结束活动后恢复原版日程。
- 矿洞护卫：NPC 使用默认银河剑主动攻击附近怪物；生命值归零后扣除半颗心、住院一天，并记录这次经历。
- 钓鱼伙伴：玩家抛竿后 NPC 完成抛竿、等待、收杆和真实捕获，鱼获交给玩家。
- 主动相遇：只在同地图 7 格内触发，不传送或追踪 NPC。
- 隔夜邮件：次日邮件可以打开、显示附件并正常领取。
- 无 API Key、错误 Key、超时和无效 JSON 都能安全失败，不阻塞保存或睡觉。

## 发布页面资料

建议准备以下内容：

- 一张 NPC 普通对话截图；
- 一张主动相遇截图；
- 一张带真实附件的惊喜邮件截图；
- 一张 AI 提供商设置界面截图；
- Stardew Valley 1.6、SMAPI 4.x、DeepSeek/OpenAI API Key 的依赖说明；
- API 内容传输、API 费用与明文 `config.json` Key 的醒目隐私说明。

## 发布前仍需人工决定

- 选择并添加项目许可证；当前仓库没有 `LICENSE`，发布前不要默认宣称开源许可。
- 获得 Nexus Mods、GitHub Releases 或其他平台的页面 ID 后，为 `manifest.json` 添加真实 `UpdateKeys`。不要填写占位 ID。
- 确认发布作者名是否继续使用 `firstmod`。

## 已知限制

- 主动社交、礼物、邮件和长期持久化只由主玩家执行。
- AI 服务需要联网，速度、可用性和费用取决于第三方提供商。
- Windows、Intel Mac 和 Apple Silicon Mac 必须分别发布独立 ZIP，不能混装不同平台的后端。macOS 二进制必须在目标 Mac 上签名和 notarize。
- 模型仍可能生成不理想的角色表达；涉及礼物的物品和结果由代码额外约束，但普通文本不能完全确定化。
