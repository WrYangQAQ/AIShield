# Agent 安全防护功能 TODO

> 本文只基于当前代码结构整理，不依赖外部论文或既有设计文档。目标是把 AIShield 从“输入/输出规则检测”继续扩展为更完整的 Agent 安全防护中间件。

## 优先级总览

| 优先级 | 功能 | 价值 | 预计改动范围 |
| --- | --- | --- | --- |
| P0 | 工具权限策略 | 防止 Agent 越权调用高危工具 | 后端模型、规则服务、工具检测接口、前端配置页 |
| P0 | 人工审批工作流 | 让 `NeedApproval` 动作真正可用 | 数据表、服务层、管理端审批页 |
| P0 | 流式输出安全拦截 | 适配 Agent 流式回复，中途发现风险可停止 | C# 流式缓冲检测、审计、接入 SDK |
| P1 | 记忆安全治理补全 | 补齐本地记忆能力的隔离、审计与管理 | Agent 隔离、审计、状态模型、前端页面 |
| P1 | 会话完整性校验 | 防止客户端篡改历史消息误导 Agent | C# 会话摘要存储、完整性校验、审计 |
| P1 | 本地算法统一审计与前端接入 | 让主题漂移、记忆、RAG 能力可观测可管理 | 审计适配、前端 API、测试页面 |
| P2 | Agent 行为异常检测 | 从单次检测升级为持续行为风控 | 审计聚合、告警、自动降级 |
| P2 | Agent Key 轮换与泄露响应 | 提升 Agent 接入密钥生命周期安全 | Agent 表、鉴权中间件、管理端 |

---

## P0-1 工具权限策略

### 背景

当前 `ToolCallGuard` 主要把 `ToolName + Arguments` 拼接成文本，然后复用输入规则进行匹配。`ToolPolicy` 模型里已经预留了 `DangerousTools`、`DangerousArgumentPatterns`、`AppToolAllowList`，但目前没有形成真正的 Agent 级工具权限体系。

### 目标

让每个 Agent 都可以配置“允许调用哪些工具、哪些工具需要审批、哪些参数模式必须拦截”，防止 Agent 因提示词注入或越权推理调用危险工具。

### 建议任务

- 新增工具策略实体，例如 `AgentToolPolicy`：
  - `Id`
  - `AgentId`
  - `ToolName`
  - `PolicyAction`: `Allow` / `Warn` / `Block` / `NeedApproval`
  - `ArgumentPattern`
  - `Description`
  - `Enabled`
  - `CreatedAt`
  - `UpdatedAt`
- 扩展 `ToolCallGuard.CheckToolCallAsync`：
  - 先检查工具白名单。
  - 再检查工具名是否命中高危工具。
  - 再检查参数 JSON 是否命中危险参数规则。
  - 最后再复用通用输入规则做兜底检测。
- 支持参数级策略：
  - 例如 `file_path` 不允许出现 `..`、系统目录、绝对路径。
  - 例如 `url` 只允许指定域名。
  - 例如 `sql` 禁止 `drop`、`truncate`、`delete without where`。
- 前端新增“工具权限”配置页：
  - 选择 Agent。
  - 展示工具列表。
  - 设置每个工具的动作。
  - 添加参数匹配规则。
- 审计中记录更清晰的命中原因：
  - 命中工具名策略。
  - 命中参数策略。
  - 命中通用安全规则。

### 验收标准

- 未配置白名单时，可以选择默认拦截未知工具或默认允许。
- 某个 Agent 被禁止的工具调用会返回 `Allowed=false`。
- 某个工具配置为 `NeedApproval` 时不会直接放行，而是进入审批流程。
- 审计记录能看出具体是哪条工具策略导致拦截。

---

## P0-2 人工审批工作流

### 背景

`SecurityAction` 已经定义了 `NeedApproval`，但当前检测服务只返回结果，没有真正的审批单、审批状态和继续执行凭证。

### 目标

当输入、输出或工具调用命中 `NeedApproval` 时，系统生成审批单，由管理员在后台确认后，Agent 才能继续执行高风险操作。

### 建议任务

- 新增审批实体，例如 `ApprovalRequest`：
  - `Id`
  - `ApprovalToken`
  - `AgentId`
  - `AgentName`
  - `SubjectHash`
  - `Direction`: `Input` / `Output` / `ToolCall` / `Memory` / `Rag`
  - `OriginalContent`
  - `ProcessedContent`
  - `RiskLevel`
  - `HitRules`
  - `Reason`
  - `Status`: `Pending` / `Approved` / `Rejected` / `Expired`
  - `Reviewer`
  - `ReviewComment`
  - `ExpiresAt`
  - `CreatedAt`
  - `ReviewedAt`
- 检测服务命中 `NeedApproval` 时：
  - 创建审批单。
  - 返回 `Allowed=false`。
  - 返回 `Action=NeedApproval`。
  - 返回 `ApprovalToken` 或 `ApprovalId`。
- 新增管理端 API：
  - 查询待审批列表。
  - 查看审批详情。
  - 通过审批。
  - 拒绝审批。
  - 查询审批状态。
- Agent 接入侧新增继续执行接口：
  - Agent 带 `ApprovalToken` 查询是否已通过。
  - 审批通过后允许一次性继续执行。
- 前端新增审批页面：
  - 风险等级筛选。
  - Agent 筛选。
  - 原始内容/工具参数展示。
  - 通过/拒绝按钮。

### 验收标准

- `NeedApproval` 不再只是返回枚举，而是产生可追踪审批单。
- 审批通过后，Agent 可以拿到明确的继续执行结果。
- 审批拒绝后，同一审批 token 不能继续使用。
- 审批动作写入审计，方便后续追责。

---

## P0-3 流式输出安全拦截

### 背景

当前 `OutputSecurityService` 适合检查完整输出，但真实 Agent 经常使用 streaming response。危险内容可能在输出过程中逐段出现，如果等完整输出结束再检查，用户可能已经看到了风险内容。

项目已经移除 Python 微服务依赖方向，因此该能力应直接在 C# 后端实现，并复用现有输出规则、内容规范化和审计体系。

### 目标

支持按 chunk 或语义片段检查流式输出，在风险内容出现时及时中断输出。

### 建议任务

- 在 `SecurityController` 增加接口：
  - `POST /api/security/check-streaming-output`
  - `POST /api/security/check-semantic-complete`
- 新增本地服务，例如：
  - `StreamingOutputSecurityService`
  - `SemanticBufferService`
- 新增 DTO：
  - `StreamingOutputCheckRequest`
  - `SemanticCompleteCheckRequest`
  - `StreamingOutputCheckResponse`
- 建议检测流程：
  - Agent 每输出一个 chunk，先进入缓冲区。
  - 判断缓冲区是否形成语义完整片段。
  - 对完整片段调用本地流式输出安全服务。
  - 若命中高危内容，立即返回 `Block`，接入方停止继续输出。
  - 最终完整回复再调用现有 `check-output` 做一次总检查。
- 审计记录：
  - 记录 chunk 索引。
  - 记录触发中断的片段。
  - 记录最终是否成功完成输出。

### 验收标准

- 流式输出中途出现敏感信息或危险指令时，可以被立即拦截。
- 前端或接入 SDK 能收到明确的 `stop_reason`。
- 审计可以定位到第几个 chunk 触发风险。

---

## P1-1 记忆安全治理补全

### 背景

当前 C# 后端已经实现记忆写入前检测、强制二次检测、数据库持久化、批量写入、冲突降权、相似候选识别、半衰期衰减和软归档。剩余重点不再是重写算法，而是补齐多 Agent 隔离、统一审计、隔离状态和管理端能力。

### 目标

让现有本地记忆能力具备清晰的租户边界、处置状态、人工复核和全生命周期审计。

### 建议任务

- 为所有 memory 接口补充统一审计：
  - `memory-write` 检测结果写入 `AuditRecords`。
  - `memory-put` 保存动作写入 `AuditRecords`。
  - `memory-bulk` 批量保存时记录每条记忆的风险结果。
- 扩展现有 `MemoryRecord`：
  - `AgentId`
  - `RiskLevel`
  - `Status`: `Active` / `Quarantined` / `Archived`
  - `ReviewedAt`
  - `Reviewer`
- 将字符串字段改为枚举或受限值：
  - `Source`: `User` / `Admin` / `System` / `Tool`
  - `RiskLevel`
- 对 `similar_content_candidate` 增加人工复核流程，而不是只返回候选结果。
- 对低置信来源或高风险记忆默认进入隔离区。
- 增加恢复归档记忆、确认候选冲突和更新正向引用时间的管理接口。
- 为定时衰减增加后台任务，避免依赖调用方逐条触发。
- 前端新增“记忆安全”页面：
  - 查看记忆元数据，不展示敏感明文。
  - 查看隔离记忆。
  - 手动归档、恢复和审核相似候选。

### 验收标准

- 不同 Agent 不能读取、修改或衰减彼此的记忆。
- 高风险或待复核记忆能够进入隔离状态。
- 管理端可以看到记忆来源、置信度、冲突记录、风险等级和状态。
- 记忆删除、归档、衰减都有审计记录。

---

## P1-2 会话完整性校验

### 背景

Agent 很依赖对话历史。如果客户端可以伪造或篡改历史消息，就可能诱导 Agent 误以为之前已经获得授权，或者误以为系统已经下达过某些指令。

当前 C# 后端还没有会话权威历史及完整性校验模块，需要作为独立的本地能力实现。

### 目标

让服务端保存权威会话历史摘要，并在关键请求前校验客户端提交的历史是否被篡改。

### 建议任务

- 在 C# 后端增加接口：
  - `POST /api/security/session/set-history`
  - `POST /api/security/check-session-integrity`
  - `DELETE /api/security/session/{sessionId}`
- 新增本地服务和数据模型：
  - `SessionIntegrityService`
  - `SessionHistoryRecord`
- DTO 增加：
  - `SessionHistoryItem`
  - `SetSessionHistoryRequest`
  - `SessionIntegrityCheckRequest`
- 检测流程：
  - 每轮对话结束后，接入方把服务端真实历史同步到本地会话历史存储。
  - 下一轮请求前，客户端提交历史摘要。
  - `SessionIntegrityService` 对比客户端历史和服务端权威历史。
  - 发现缺失、插入、篡改时阻断请求。
- 审计记录：
  - `Direction` 可以新增 `Session`。
  - 记录 `SessionId` 或其 hash。
  - 不保存完整聊天明文，只保存摘要和风险原因。

### 验收标准

- 客户端删除历史消息时可以被检测。
- 客户端插入伪造管理员消息时可以被检测。
- 检测失败会进入审计，并能按 Agent/SubjectHash 查询。

---

## P1-3 本地算法统一审计与前端接入

### 背景

主题漂移、记忆安全和 RAG 重排已经由 C# 本地服务实现，但这些接口尚未全部写入统一审计，也没有管理端页面用于调用和查看结果。

### 目标

让本地算法和现有输入、输出、工具检测一样可追踪、可查询、可测试。

### 建议任务

- 扩展 `AuditDirection`：
  - `TopicDrift`
  - `Memory`
  - `Rag`
- 为本地算法增加专用审计写入方法。
- 主题漂移审计记录片段数量、连续漂移数和触发索引。
- 记忆审计记录写入/更新/冲突/归档/衰减动作，不保存不必要的正文。
- RAG 审计记录候选数量、过滤数量、重排模式和最高风险。
- 前端 API 层增加主题漂移、记忆和 RAG 接口。
- 前端增加本地算法测试或管理页面。
- Dashboard 增加记忆冲突、主题漂移和 RAG 过滤统计。

### 验收标准

- 三类本地算法调用均能生成可查询审计记录。
- 管理端可以测试主题漂移和 RAG 重排，并管理记忆状态。
- Dashboard 可以展示本地算法产生的风险事件。

---

## P2-1 Agent 行为异常检测

### 背景

当前审计系统已经能记录单次输入、输出、工具调用的风险结果，也能做一些趋势统计。但 Agent 安全不只看单次请求，还要看一段时间内的行为模式。

### 目标

基于审计记录发现异常 Agent、异常用户主体或异常工具调用模式，并支持告警或自动降级。

### 建议任务

- 新增异常检测服务，例如 `AgentRiskMonitorService`。
- 基于 `AuditRecords` 做滑动窗口统计：
  - 5 分钟内高危事件数量。
  - 1 小时内工具调用拦截率。
  - 同一 `SubjectHash` 连续注入次数。
  - 某 Agent 风险率突然升高。
- 新增风险状态：
  - `Normal`
  - `Watch`
  - `Restricted`
  - `Disabled`
- 自动动作：
  - 风险升高时把高危工具改为 `NeedApproval`。
  - 达到阈值时临时禁用 Agent。
  - 触发告警记录。
- 前端展示：
  - Agent 风险状态。
  - 最近异常原因。
  - 手动恢复按钮。

### 验收标准

- 连续高危工具调用会触发异常状态。
- 同一主体连续 prompt injection 会被标记。
- 管理端能看到异常原因和建议处置方式。

---

## P2-2 Agent Key 轮换与泄露响应

### 背景

`AgentService` 已经对 Agent Key 做了 salt hash、fingerprint 和 preview，基础设计是安全的。但 Agent Key 一旦泄露，目前只能禁用或删除整个 Agent，缺少平滑轮换和泄露响应能力。

### 目标

支持 Agent Key 的生成、轮换、吊销、过渡期和异常使用告警。

### 建议任务

- 新增 `AgentKeys` 表，替代单 Agent 单 key：
  - `Id`
  - `AgentId`
  - `KeyHash`
  - `KeySalt`
  - `KeyFingerprint`
  - `KeyPreview`
  - `Status`: `Active` / `Deprecated` / `Revoked`
  - `CreatedAt`
  - `ExpiresAt`
  - `LastUsedAt`
  - `LastUsedIp`
- 修改鉴权逻辑：
  - 根据 key fingerprint 找到 key。
  - 再关联 Agent。
  - key 和 Agent 都启用时才允许访问。
- 增加管理端功能：
  - 生成新 key。
  - 设置旧 key 过期时间。
  - 立即吊销 key。
  - 查看 key 使用记录。
- 异常检测：
  - 同一 key 短时间出现在多个 IP。
  - 长期不用的 key 突然大量请求。
  - key 命中大量高危规则。

### 验收标准

- 可以在不中断 Agent 的情况下生成新 key 并逐步废弃旧 key。
- 被吊销 key 无法继续访问 `/api/security/*`。
- key 的最近使用时间和 IP 可以在管理端查看。

---

## 建议实施顺序

1. 工具权限策略。
2. 人工审批工作流。
3. 流式输出安全拦截。
4. 记忆安全治理补全。
5. 会话完整性校验。
6. 本地算法统一审计与前端接入。
7. Agent 行为异常检测。
8. Agent Key 轮换与泄露响应。

这个顺序的原因是：工具权限和审批仍是当前最大的执行控制缺口；流式输出覆盖真实 Agent 回复场景；现有 C# 记忆、主题漂移和 RAG 能力接下来应重点补齐隔离、审计和前端管理，而不再重复实现算法。
