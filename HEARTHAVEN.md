# 炉心 · Hearthaven

WPF 原生 AI Agent 桌面应用（不使用 WebView），追求极致性能。

---

## 三层架构依赖关系

```
Hearthaven (WPF) ───→ Hearthaven.Core
                ───→ Hearthaven.Data ───→ Hearthaven.Core
```

- **Core 层**：纯 .NET 9，定义所有接口和领域模型
- **Data 层**：引用 Core，实现 Repository 接口
- **UI 层**：引用 Core + Data，负责依赖注入

---

## 核心流程：Agent Loop

```
用户输入
    │
    ▼
ChatViewModel.SendAsync()
    │  创建 MessageDisplayModel（用户气泡 + 助手气泡）
    │  调用 _agentService.RunAsync()
    │
    ▼
AgentService.RunAsync()
    │
    ├─ 1. 保存用户消息到 DB (IMessageRepository)
    ├─ 2. ContextManager.BuildContextAsync()
    │      ├─ 从 DB 加载历史消息
    │      ├─ 追加新用户消息
    │      ├─ 添加 system prompt
    │      └─ Token 超预算 → 裁剪最早消息
    │
    ├─ 3. while (tool_calls detected)
    │      ├─ Provider.StreamChatWithToolsAsync()
    │      ├─ 流式接收 TextChunk / ThinkingChunk / ToolCallChunk
    │      ├─ 通过 AgentEvents 回调通知 UI 更新
    │      ├─ 检测到 tool_calls →
    │      │    ├─ ToolDispatcher.ExecuteBatchAsync()
    │      │    ├─ 结果注入上下文
    │      │    ├─ ✨ [A8] 检查追加消息 → 有则注入上下文
    │      │    └─ continue
    │      └─ 无 tool_calls → break
    │
    └─ 4. 返回 AgentResult
    │
    ▼
ChatViewModel ← AgentEvents 回调
    │  OnTextChunk       → 追加到 textBuffer → 批量更新 RoundBlock.Content
    │  OnThinkingChunk   → 更新 ReasoningBlock
    │  OnToolCallStart   → 添加 ToolCallBlock
    │  OnToolCallEnd     → 更新 ToolCallBlock 结果
    │
    ▼
    IsStreaming = false → MarkdownViewer 渲染
```

---

## 核心组件说明

### AgentService (`Hearthaven.Core/Agent/AgentService.cs`)
- AI 对话循环核心，不依赖任何 WPF 类型
- 接收 `AgentEvents` 回调通知 UI 更新
- 每轮工具调用后自动保存上下文到 DB

### ContextManager (`Hearthaven.Core/Agent/ContextManager.cs`)
- 使用 SharpToken（cl100k_base）进行 Token 计数
- 默认 MaxContextTokens = 65536, MaxResponseTokens = 2000
- 裁剪策略：从最早的消息开始移除，保留 system prompt

### ToolDispatcher (`Hearthaven.Core/Agent/ToolDispatcher.cs`)
- 封装工具查找、执行、批量调度
- 统一 `ToolResult` 返回执行结果（含 IsError 标记）

### ChatFlowOrchestrator (`Hearthaven/ViewModels/ChatFlowOrchestrator.cs`)
- 消息发送、重新生成、错误重试、追加消息等核心对话流程
- 不持有对 ChatViewModel 的引用，通过委托和参数解耦

### ChatViewModel (`Hearthaven/ViewModels/ChatViewModel.cs`)
- 约 425 行（重构前 1093 行）
- 职责：UI 状态管理 + 调用 AgentService + 处理回调
- 不再包含任何 DB 操作或 Agent Loop 逻辑
- 消息构建委托给 MessageBuilder，缓存管理委托给 SessionCache
- 流式 UI 更新委托给 StreamUpdater，会话生命周期委托给 SessionService

### 消息时间线 (TimelineItems)
- 每个助手消息气泡包含一个 `TimelineItems` 集合（类型 `ObservableCollection<ITimelineItem>`）
- 实际只存放 `RoundBlock` 对象，`RoundBlock` 内部再包含 `Reasoning?`（推理块）和 `ToolCalls`（工具调用块集合）
- 示例：`[RoundBlock: 思考块] → [RoundBlock: 工具调用1] → [RoundBlock: 工具调用2: 完成] → [RoundBlock: 文本回复]`

---

## 技术选型

| 层级 | 技术 | 版本 |
|:----|:-----|:----:|
| 框架 | .NET 9 WPF | 9.0 (TFM: `net9.0-windows10.0.19041.0`) |
| 数据库 | SQLite + EF Core | 9.0 |
| Markdown 渲染 | Markdig (自研 WPF 渲染器) | 0.40.0 |
| Token 计数 | SharpToken | 2.0.6 |
| MVVM | CommunityToolkit.Mvvm | 8.4.2 |
| 原生通知 | CommunityToolkit.WinUI.Notifications | 7.1.2 |
| 配置 | appsettings.json | - |

---

## 配置说明

所有用户数据统一保存在 `%APPDATA%\Hearthaven\`（Windows）目录，包括配置文件、对话数据库和 Agent 设定文件。

```
%APPDATA%\Hearthaven\
├── appsettings.json    # API 配置、模型列表、Token 参数
├── Hearthaven.db       # 对话数据库
├── agent.json          # Agent 名称/身份/称呼/头像
├── SOUL.md             # 性格设定（自由编辑）
├── AGENT.md            # 自定义 prompt 后缀（自由编辑）
└── avatar.png          # 头像图片
```

首次运行自动创建该目录和默认文件。配置采用实例类 `HearthavenSettings`（位于 `Hearthaven.Core/Settings/`），启动时从 `appsettings.json` 加载，通过构造函数注入到各 Service/ViewModel。

`appsettings.json` 示例：
```json
{
  "Endpoint": "https://api.deepseek.com",
  "DefaultModel": "deepseek-v4-flash",
  "EnableDebugLog": false,
  "Models": [
    {
      "Name": "deepseek-v4-flash",
      "Endpoint": "https://api.deepseek.com",
      "ApiKey": "你的 API Key",
      "MaxContextTokens": 65536,
      "MaxTokens": 8192
    }
  ],
  "UI": { ... }
}
```


### 设置窗口

应用内提供图形化设置界面（`设置 > 🧑 Agent / 🎨 界面 / ℹ️ 关于`），修改后保存到 `appsettings.json`（服务商/UI 配置）或 `agent.json`（Agent 名称/身份/称呼）。角色性格和额外行为指令通过编辑用户目录下的 `SOUL.md` / `AGENT.md` 生效。

### 调试日志

将 `appsettings.json` 中的 `EnableDebugLog` 设为 `true` 并重启应用，诊断日志将自动写入用户目录下的 `debug_log.txt`。日志记录关键操作点的调用链和堆栈信息，用于排查异常行为。排查完毕后记得设为 `false` 关闭，避免日志文件持续增长。

---

## Version Control

项目使用 Git 进行版本控制，当前为本地仓库（未推送到 GitHub）。

```
git log --oneline
211c481 feat: MainWindow.xaml 拆分 + 设置窗口
4c0ec0b refactor: 目录结构调整 — 拆分 ViewModels 至 Services/Models/Utilities
4fc2b31 ✨ 气泡底部操作栏 + 表格渲染修复 + UI 虚拟化
5501430 ♻️ Step 4+5: 抽取 AgentService + 瘦身 ChatViewModel
e35bc1a ♻️ Step 3: 抽取 ContextManager 上下文管理器
544f382 ♻️ Step 2: 抽取 ToolDispatcher 工具调度器
aa7bd03 ♻️ Step 1: 抽取 Repository 数据访问层
d72e2d2 🎨 清爽极简风 UI 全面美化
ec77ccd 🎉 初始提交：炉心 Hearthaven 项目骨架
```

---

## 开发状态

| 阶段 | 内容 | 状态 |
|:----|:-----|:----:|
| Phase 0 — 项目骨架与底层通信 | 项目结构、依赖注入、EF Core 数据库 | ✅ 完成 |
| Phase 1 — 核心对话循环 | AI 对话、流式输出、Markdown 渲染 | ✅ 完成 |
| Phase 2 — 核心对话流 🏁 | 消息操作、追加消息、错误重试、告警、空状态 | ✅ 完成 |
| Phase 3 — 工具调用系统 | 9 个工具 + 框架完备 | ✅ 完成 |
| Phase 4 — 目录重构与 UI 拆分 | ViewModels 拆分, MainWindow 拆分, 设置窗口 | ✅ 完成 |
| Phase 5 — 工具栏与模式切换 | 模式切换、模型切换、工作目录切换、工作目录显示 | ✅ 完成 |
| Phase 6 — 工具扩展 | 网页搜索、网页查看、技能系统、工具开关 | ⏳ |
| Phase 7 — 记忆系统 | 对话摘要、长期记忆、记忆检索 | ⏳ |
| Phase 8 — 锦上添花 | 重命名/快捷键/拖拽/全局搜索/暗色主题/多服务商/导出/输入体验/置顶 | ⏳ |

> 当前 Phase 任务追踪见 [`TODO.md`](./TODO.md)。

---

## 构建与运行

```bash
cd Hearthaven
# 先配置 API Key（编辑 Hearthaven/appsettings.json）
# 按 F5 运行（VS）或：
dotnet run --project Hearthaven/Hearthaven.csproj
```

---

## 备注

- 项目要求严格代码规范，不留技术债，不打补丁
- 所有跨层调用通过接口
- 重构后 ChatViewModel 不再直接操作 DB 和工具调度
- AgentService 可独立测试（不依赖 WPF）
- 支持 Markdown 表格渲染（管道表格 + 网格表格）
- 消息列表启用 VirtualizingStackPanel 虚拟化，长对话无性能问题
- 通知采用 Windows 原生 Toast，不是 AI 工具。当窗口最小化时，Agent 回复完成后自动弹出通知
- TFM 已升级至 `net9.0-windows10.0.19041.0`，支持直接调用 WinRT API
