# Vivant Valley 0.13.0 Release

本文档用于发布前核验，不需要放入最终 Mod 压缩包。

## 发布文件

正式附件名称：

```text
VivantValley-Release.zip
```

压缩包应只包含以下运行文件：

```text
VivantValley/
  manifest.json
  VivantValley.dll
  VivantValley.pdb
  assets/
    social/
      gift-pools.json
```

不得包含 `config.json`、API Key、源代码、测试输出、备份目录或旧的 `StardewAIMemories.dll`。

## 发布说明

**Vivant Valley** 让星露谷村民记住与玩家的长期交流，并根据当前存档事实、近期活动和彼此关系自然回应。村民可以在原版日程中主动搭话，通过经过代码验证的白名单送出有意义的当面礼物，也可能在一次温暖的交谈后于次日寄来惊喜邮件。

0.13.0 是原 **Stardew AI Memories** 的正式改名版本。现有用户的存档记忆保持兼容，但升级时必须移除旧安装目录，只保留新的 `Mods/VivantValley`，并将旧 `config.json` 复制到新目录。

## 发布前自动检查

```powershell
dotnet build .\VivantValley.csproj -c Release
dotnet run --project .\tests\ConversationEngineSmoke\ConversationEngineSmoke.csproj -c Release
.\scripts\package.ps1 -Configuration Release
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
- 模型仍可能生成不理想的角色表达；涉及礼物的物品和结果由代码额外约束，但普通文本不能完全确定化。
