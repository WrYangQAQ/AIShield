# AIShield 已实现功能清单

> 本文根据当前代码实现整理，记录项目已经落地的功能范围、主要入口和当前边界，便于后续和 `todo.md` 中的待办功能对照。

## 1. 管理端登录与鉴权

### 已实现能力

- 支持本地管理员密码登录。
- 登录成功后返回管理端 JWT。
- 管理端接口统一要求 `Bearer Token`。
- 安全检测接口支持两种身份：
  - Agent 使用 `X-API-Key` 调用。
  - 管理端测试时使用 `Bearer Token + X-Agent-Id` 调用。
- 后端中间件会把通过鉴权的 Agent 信息写入 `HttpContext.Items`，供后续安全检测和审计使用。

### 主要代码入口

- `AuthController`
- `AuthService`
- `JwtService`
- `ApiKeyAuthMiddleware`

### 当前边界

- 管理员账号体系较轻量，目前主要是单一配置密码。
- 还没有多管理员、角色权限、操作员审计等能力。

---

## 2. Agent 接入管理

### 已实现能力

- 支持注册新的 Agent。
- 注册时生成只展示一次的 Agent Key。
- Agent Key 不明文入库，使用 salt hash 保存。
- 使用 key fingerprint 辅助数据库定位，再用带盐 hash 做最终校验。
- 管理端可以查看 Agent 列表。
- 支持查询单个 Agent。
- 支持启用/禁用 Agent。
- 支持修改 Agent 名称和使用场景。
- 支持删除 Agent。
- Agent 注册时会自动绑定当前已有的全部安全规则。
- Agent 鉴权成功后会更新 `LastUsedAt`。

### 主要代码入口

- `AgentController`
- `AgentService`
- `AgentRepository`
- `AgentApp`
- `AgentRule`

### 当前边界

- 一个 Agent 当前对应一个主 key 信息。
- 还没有 Agent Key 轮换、多 key 并存、key 过期时间和 key 使用 IP 记录。
- Agent 的“场景”目前主要是描述字段，还没有直接参与策略判定。

---

## 3. 输入安全检测

### 已实现能力

- 提供输入检测接口：`POST /api/security/check-input`。
- 检查请求体是否为空。
- 检查输入内容是否为空。
- 对输入长度做上限限制。
- 按当前 Agent 绑定的输入规则执行检测。
- 支持关键词匹配和正则匹配。
- 支持风险等级聚合。
- 支持安全动作：
  - `Allow`
  - `Warn`
  - `Block`
  - `NeedApproval`
- 输入检测会生成多种检测文本变体：
  - 原文。
  - 规范化文本。
  - URL Decode。
  - HTML Decode。
  - Base64 Decode。
- 规范化逻辑包括：
  - Unicode FormKC 归一化。
  - 全角 ASCII 转半角。
  - 多余空白折叠。
  - 小写化。

### 主要代码入口

- `SecurityController.CheckInput`
- `InputSecurityService`
- `ContentNormalizer`
- `RuleEngine`
- `RuleConfigService.GetInputRulesAsync`

### 当前边界

- `NeedApproval` 当前只是检测结果动作之一，还没有真实审批单流程。
- 输入检测主要基于规则匹配，暂未接入语义模型判断。

---

## 4. 输出安全检测与脱敏

### 已实现能力

- 提供输出检测接口：`POST /api/security/check-output`。
- 对 Agent 或模型输出内容进行规则检测。
- 按当前 Agent 绑定的输出规则执行检测。
- 支持关键词匹配和正则匹配。
- 支持风险等级聚合。
- 支持输出脱敏动作 `Mask`。
- 当规则动作为 `Mask` 时，会用规则配置的 `Replacement` 替换命中的敏感内容。
- 当规则动作为 `Block` 时，会返回不允许继续输出。
- 空输出会被视为无需处理。

### 主要代码入口

- `SecurityController.CheckOutput`
- `OutputSecurityService`
- `RuleEngine.ReplaceSensitiveContent`
- `RuleConfigService.GetOutputRulesAsync`

### 当前边界

- 当前适合完整输出检查。
- 尚未在 .NET 侧实现流式 chunk 级安全检测。
- 输出规则没有使用 `ContentNormalizer` 的多变体检测。

---

## 5. 工具调用安全检测

### 已实现能力

- 提供工具调用检测接口：`POST /api/security/check-tool-call`。
- 检查工具名是否为空。
- 将工具名和参数 JSON 拼接成检测内容。
- 复用当前 Agent 的输入规则进行风险检测。
- 命中规则后返回风险等级、动作、原因和命中规则编号。
- 工具调用检测结果会写入审计记录，方向为 `ToolCall`。

### 主要代码入口

- `SecurityController.CheckToolCall`
- `ToolCallGuard`
- `ToolCallCheckRequest`
- `AuditService.AddToolCallRecordAsync`

### 当前边界

- 目前还没有真正启用 `ToolPolicy` 中预留的工具白名单、危险工具列表和危险参数规则。
- 还没有参数 schema 校验。
- 还没有工具级审批、工具级限流和工具级权限配置页面。

---

## 6. 安全规则管理

### 已实现能力

- 支持从 `security-rules.json` 初始化默认规则到数据库。
- 支持查询全部规则。
- 支持按 Agent 查询已启用规则。
- 支持新增规则。
- 支持修改规则。
- 支持删除规则。
- 支持启用/禁用全局规则。
- 支持启用/禁用某个 Agent 绑定的规则。
- 支持新增规则时绑定到指定 Agent。
- 支持测试单条规则是否命中指定内容。
- 支持前端获取固定枚举选项：
  - 规则类型。
  - 匹配类型。
  - 风险等级。
  - 安全动作。

### 主要代码入口

- `RulesController`
- `RuleConfigService`
- `RuleEngine`
- `SecurityRuleRepository`
- `SecurityRule`
- `SecurityRuleSet`

### 当前边界

- 规则类型当前主要覆盖 `Input` 和 `Output`。
- `ToolPolicy` 在规则集结构中存在，但还没有完整管理能力。
- 规则校验已经限制输入规则不能使用 `Mask`，输出 `Mask` 必须填写替换文本。

---

## 7. 审计记录

### 已实现能力

- 输入检测、输出检测、工具调用检测都会生成审计记录。
- 审计记录包含：
  - AgentId。
  - AgentName。
  - SubjectHash。
  - 创建时间。
  - 检测方向。
  - 原始内容。
  - 处理后内容。
  - 风险等级。
  - 处理动作。
  - 命中规则。
  - 原因。
  - 客户端 IP。
  - 检测耗时。
- 支持查询近期审计记录。
- 支持按 Agent 过滤近期记录。
- 支持分页搜索审计记录。
- 搜索支持多维过滤：
  - Agent。
  - 检测方向。
  - 风险等级。
  - 处理动作。
  - 命中规则。
  - 关键词。
  - 时间范围。

### 主要代码入口

- `AuditController`
- `AuditService`
- `AuditRecordRepository`
- `AuditRecord`
- `AuditSearchRequest`

### 当前边界

- 当前审计方向包括 `Input`、`Output`、`ToolCall`。
- 主题漂移、记忆和 RAG 本地算法接口目前还没有全部纳入同一套 `AuditRecords` 记录。
- 原始内容会入库，后续如要更强隐私保护，可改为 hash + 截断片段。

---

## 8. Dashboard 风险看板

### 已实现能力

- 支持查询指定 Agent 的今日总览指标：
  - 今日请求数。
  - 今日拦截数。
  - 今日脱敏数。
  - 今日高风险事件数。
- 支持查询风险趋势：
  - 最近 N 天拦截数量。
  - 最近 N 天脱敏数量。
  - 最近 N 天高风险事件数量。
  - 缺失日期自动补 0。
- 支持查询 Agent 健康状态：
  - 健康分。
  - 平均响应时间。
  - 错误率。
  - 可用率。

### 主要代码入口

- `DashboardController`
- `AuditService.QueryOverviewAsync`
- `AuditService.QueryRiskTrendAsync`
- `AuditService.QueryHealthStatusAsync`

### 当前边界

- 健康分目前基于审计中的拦截和严重风险做粗略计算。
- 还没有异常检测、自动告警和自动处置。

---

## 9. 本地 Agent 安全算法

### 主题漂移检测

- 提供接口：`POST /api/security/check-topic-drift`。
- 使用原始问题提取主题锚点。
- 对后续生成片段进行中英文 token 化。
- 综合计算锚点覆盖率和 Jaccard 相似度。
- 支持连续低相关片段阈值。
- 支持最低相似度和最短有效片段配置。
- 达到连续漂移阈值时返回阻断结果和 `topic_drift` 风险标签。
- 返回每个片段的相关度分数及低相关判定。

### 记忆安全与生命周期管理

- 提供记忆写入前检测接口：`POST /api/security/memory-write`。
- 复用当前 Agent 的输入安全规则检查记忆投毒内容。
- `memory-put` 写入接口会再次强制执行安全检测，不能通过跳过预检绕过防护。
- 支持单条记忆新增和更新：`POST /api/security/memory-put`。
- 支持最多 500 条记忆的批量写入：`POST /api/security/memory-bulk`。
- 支持查询记忆元数据且不返回正文：`GET /api/security/memory/{memoryId}`。
- 支持软删除和记录归档原因：`DELETE /api/security/memory/{memoryId}`。
- 支持基于半衰期的置信度衰减：`POST /api/security/memory-decay`。
- 置信度低于遗忘阈值时自动软归档。
- 使用 SHA256 内容哈希识别完全重复内容。
- 支持保存可选向量，并在缺少向量时使用文本相似度降级。
- 支持基于 `MemoryKey` 的确定冲突检测：
  - 相同来源和业务槽位出现新值时，对旧记忆降权。
  - 旧记忆置信度过低时自动归档。
- 支持潜在相似记忆检测：
  - 没有相同业务槽位证据时只标记为候选。
  - 不会直接修改候选旧记忆。
- 使用数据库并发版本防止并行更新或衰减相互覆盖。

### RAG 安全过滤与重排

- 提供接口：`POST /api/security/rag-rerank`。
- 过滤空文档和包含常见提示词注入、越权指令特征的候选文档。
- 支持查询向量和候选文档向量的余弦相似度计算。
- 支持 token Jaccard 文本相关度。
- 支持由服务端配置不同来源的信任分。
- 有有效向量时使用语义、词法和来源信任度进行混合评分。
- 没有有效向量时自动使用词法降级模式。
- 支持最低保留分数和最大返回数量。
- 返回实际重排模式、过滤数量和按综合得分排序的候选列表。

### 公共算法能力

- 支持 Unicode FormKC 归一化和空白折叠。
- 支持中英文 token 提取。
- 支持 Jaccard 相似度、锚点覆盖率和向量余弦相似度。
- 内置常见 prompt injection 和越权指令特征检测。
- 算法参数统一通过 `GuardAlgorithms` 配置节管理。

### 主要代码入口

- `SecurityController`
- `MemoryService`
- `RagRerankService`
- `TopicDriftService`
- `GuardTextAlgorithmHelper`
- `GuardAlgorithmDtos`
- `MemoryRecord`

### 当前边界

- 主题漂移、记忆和 RAG 算法已经不依赖外部 Python 服务。
- 尚未实现会话完整性和流式输出检测的本地 C# 版本。
- 主题漂移、记忆和 RAG 接口还没有全部纳入统一审计。
- 这些本地算法能力目前还没有接入前端管理页面。
- 内置危险模式属于规则型快速筛查，后续仍可扩展配置化模式或语义模型。

---

## 10. 限流与基础中间件

### 已实现能力

- 后端 API 使用固定窗口限流。
- 限流范围覆盖 `/api` 请求。
- `OPTIONS` 请求会跳过限流。
- 限流 key 优先使用已鉴权的 Agent 信息。
- 如果没有 Agent 信息，则退化为 IP 限流。
- 定期清理过期计数器，避免内存持续增长。
- 鉴权中间件在限流中间件之前执行，减少未授权请求干扰 Agent 限流计数。

### 主要代码入口

- `RateLimitMiddleware`
- `ApiKeyAuthMiddleware`
- `Program.cs`

### 当前边界

- 限流配置是全局级别。
- 还没有按 Agent、按接口、按工具单独配置不同限流策略。
- 当前计数器是进程内存结构，暂不适合多实例共享限流状态。

---

## 11. 数据库与数据模型

### 已实现能力

当前 EF Core 已配置以下核心表：

- `Agents`
  - 保存 Agent 基础信息和 Agent Key 安全字段。
- `SecurityRules`
  - 保存输入/输出安全规则。
- `AgentRules`
  - 保存 Agent 和规则的绑定关系。
- `AuditRecords`
  - 保存安全检测审计记录。
- `MemoryRecords`
  - 保存记忆正文、内容哈希、来源、业务槽位、置信度、可选向量、衰减时间和软归档状态。

### 已实现的模型约束

- Agent Key fingerprint 建唯一索引。
- 规则编号 `RuleId` 建唯一索引。
- Agent 和规则绑定为多对多关系。
- 删除 Agent 或规则时级联删除绑定关系。
- 审计记录对 `AgentId` 和 `CreatedAt` 建索引。
- 记忆记录对内容哈希、更新时间以及来源/业务槽位/归档状态建立索引。
- 记忆记录使用 `RowVersion` 做乐观并发控制。

### 主要代码入口

- `AppDbContext`
- `Migrations`
- `Models`
- `Repositories`

### 当前边界

- 还没有审批表、工具策略表、Agent Key 独立表和告警表。
- `MemoryRecords` 目前没有直接关联 `AgentId`，多 Agent 记忆隔离仍需要加强。

---

## 12. 前端管理台

### 已实现页面

- 登录页：`LoginView.vue`
- Agent 注册页：`RegisterView.vue`
- Dashboard 总览页：`DashboardView.vue`
- Agent 接入管理页：`AgentAccessView.vue`
- 输入检测测试页：`InputCheckView.vue`
- 输出过滤测试页：`OutputFilterView.vue`
- 工具调用检测测试页：`ToolGuardView.vue`
- 规则管理页：`RulesView.vue`
- 审计日志页：`AuditLogView.vue`
- 接入指南页：`GuideView.vue`

### 已接入 API

- 管理员登录。
- Agent 注册、列表、修改、启用禁用、删除。
- 输入检测。
- 输出检测。
- 工具调用检测。
- 规则查询、新增、修改、启用禁用、删除、测试。
- 审计列表和审计搜索。
- Dashboard 风险趋势、健康状态和今日总览。

### 主要代码入口

- `Aishield.Frontend/src/router/index.ts`
- `Aishield.Frontend/src/api/services.ts`
- `Aishield.Frontend/src/api/types.ts`
- `Aishield.Frontend/src/views`
- `Aishield.Frontend/src/layouts/MainLayout.vue`

### 当前边界

- 前端还没有接入主题漂移、记忆安全、流式输出、RAG 重排、审批流、工具策略等页面。
- 当前工具防护页面主要是测试工具调用检测，不是完整工具权限管理。

---

## 13. 当前整体能力总结

当前项目已经实现了一个可运行的 Agent 安全加固中间件基础版本，核心能力包括：

- Agent 接入和 API Key 鉴权。
- 管理端 JWT 登录。
- 输入安全检测。
- 输出安全检测和脱敏。
- 工具调用前检测。
- 可配置安全规则。
- Agent 级规则绑定。
- 安全审计记录。
- Dashboard 风险统计。
- 基础 API 限流。
- C# 本地主题漂移检测。
- C# 本地记忆安全、冲突检测、持久化和置信度衰减。
- C# 本地 RAG 注入过滤与混合重排。
- Vue 管理台基础页面。

从代码现状看，项目已经从“规则驱动的安全检测网关”扩展到了部分有状态 Agent 安全能力，包括记忆生命周期、主题漂移和 RAG 安全重排。后续可以继续向 `todo.md` 中规划的工具权限、审批、流式拦截、会话完整性、统一审计和行为异常检测扩展。
