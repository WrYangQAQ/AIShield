# CuteBlogGuard 安全护栏微服务

内容安全防御算法库的 FastAPI 封装，23 个 API 端点覆盖检测、管理、运维全链路。

## 快速开始

```bash
# 1. 安装依赖
pip install -r requirements.txt

# 2. 配置环境变量（可选）
cp .env.example .env
# 编辑 .env，填入 OPENAI_API_KEY 等（留空则使用本地 Embedder + 规则快筛）

# 3. 启动服务
python start.py
# 或
uvicorn guard_service:app --host 0.0.0.0 --port 8900
```

服务启动后访问 `http://127.0.0.1:8900/docs` 查看交互式 API 文档。

> ⚠️ Windows 用户请用 `127.0.0.1` 而非 `localhost`，避免 IPv6 解析延迟。

## API 端点一览

### 检测类（/guard/*）

| 端点 | 方法 | 功能 |
|------|------|------|
| `/guard/input` | POST | 输入侧安全检测（编码归一化→PII脱敏→规则快筛→语义审核） |
| `/guard/tool-call` | POST | 工具调用参数校验（白名单→格式校验→注入扫描） |
| `/guard/content` | POST | 用户内容审核（评论/留言等） |
| `/guard/session-integrity` | POST | 会话完整性校验（客户端历史vs服务端权威历史） |
| `/guard/topic-drift` | POST | 主题漂移检测（锚点实体追踪） |
| `/guard/streaming` | POST | 流式输出安全拦截（缓冲→检测→推流/中断） |
| `/guard/audit` | POST | 安全审计日志（hash+截断片段，不存明文） |
| `/guard/memory-write` | POST | 记忆写入安全网关（写入前同等强度检测） |
| `/guard/rag-rerank` | POST | RAG 安全重排（注入过滤+信任度加权） |
| `/guard/semantic-complete` | POST | 语义完整性判断（流式缓冲辅助） |
| `/guard/trust-level` | POST | 信任等级解析（来源隔离+提权识别） |
| `/guard/memory-decay` | POST | 记忆衰减（指数衰减+低置信度归档） |

### 管理类（/manage/*）

| 端点 | 方法 | 功能 |
|------|------|------|
| `/manage/session/set-history` | POST | 设置会话历史（服务端权威数据） |
| `/manage/session/{session_id}` | DELETE | 清除会话历史 |
| `/manage/trust/set-level` | POST | 设置信任等级 |
| `/manage/trust/{api_key}` | DELETE | 删除信任等级 |
| `/manage/memory/put` | POST | 存入单条记忆（含 embedding + 冲突检测） |
| `/manage/memory/bulk` | POST | 批量初始化记忆（两阶段：embed→冲突检测，单次≤500条） |
| `/manage/memory/{memory_id}` | GET | 查询记忆条目 |
| `/manage/memory/{memory_id}` | DELETE | 删除记忆条目 |
| `/manage/audit/logs` | GET | 查询审计日志 |

### 运维类

| 端点 | 方法 | 功能 |
|------|------|------|
| `/guard/config` | GET | 查看当前运行时配置（只读） |
| `/guard/audit/stats` | GET | 审计日志统计 |
| `/health` | GET | 健康检查+依赖状态 |

## 统一返回格式

所有端点返回相同结构，集成方只需一套解析逻辑：

```json
{
  "Success": true,
  "Message": "ok",
  "Data": {
    "requestId": "a1b2c3d4",
    "blocked": false,
    "riskLabel": null,
    "details": {}
  },
  "Record": {
    "time": "2026-06-11T15:30:00+00:00",
    "agentKey": "ai-chat",
    "operation": "guard_input",
    "requestId": "a1b2c3d4"
  }
}
```

- `Success=true` → 放行，`blocked=false`
- `Success=false` → 阻断，`blocked=true`，`riskLabel` 含风险分类
- 任何内部异常也返回此格式（`Success=false`，不会裸抛 500）

## 鉴权

设置环境变量 `GUARD_SERVICE_API_KEY` 后，所有 `/guard/*` 请求需携带：

```
X-Guard-Key: sk-guard-xxxx
```

未设置则不鉴权（仅限内网/开发环境）。

## Embedding 模式

服务启动时自动选择 Embedding 模式：

| 条件 | 模式 | 说明 |
|------|------|------|
| `EMBEDDING_API_KEY` 或 `OPENAI_API_KEY` 已设置 | 远程 Embedding API | 无 GIL 问题，推荐生产使用 |
| 两者均为空 | 本地 fastembed（子进程隔离） | 无需 API Key，子进程 + threading.Event 隔离 GIL |

> ⚠️ 本地模式每个 uvicorn worker 会启动一个 Embedder 子进程（约 300MB）。多 worker 部署时注意内存。

## .NET 集成示例

```csharp
using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Guard-Key", "sk-guard-xxxx");

var resp = await http.PostAsJsonAsync("http://127.0.0.1:8900/guard/input", new {
    input = "用户输入的文本",
    agentKey = "ai-chat",
    strictMode = true
});

var result = await resp.Content.ReadFromJsonAsync<GuardResult>();
if (!result.Success) {
    // 阻断逻辑
    return StatusCode(403, result.Message);
}
// 放行
```

```csharp
public class GuardResult {
    public bool Success { get; set; }
    public string Message { get; set; }
    public GuardData? Data { get; set; }
    public GuardRecord? Record { get; set; }
}

public class GuardData {
    public string RequestId { get; set; }
    public bool Blocked { get; set; }
    public string? RiskLabel { get; set; }
    public Dictionary<string, object>? Details { get; set; }
}
```

## 演示脚本

不启动服务，直接调用算法库：

```bash
python demo.py
```

## 测试

### 算法库单元测试（无需启动服务）

```bash
pip install pytest pytest-asyncio
pytest guard_algorithms/tests/ -v
```

### API 集成测试（需先启动服务）

```bash
# 启动服务
python start.py

# 另开终端运行测试
python test_single_hook.py          # 单条记忆写入计时
python test_conflict_simple.py      # 极简冲突检测验证
python test_embedding_stress.py     # 千条级压力测试+冲突检测验证
python test_embedding_full.py       # Embedder 全量功能测试
python diag_embedder.py             # Embedder 诊断工具
```

## Docker 部署

```bash
docker build -t guard-service .
docker run -d -p 8900:8900 --env-file .env guard-service
```

> ⚠️ 默认单 worker（无 API Key 时使用本地 Embedder，多 worker 会倍增内存）。  
> 配置了远程 Embedding API 后可用 `--workers 4` 提高并发。

## 环境变量

见 [.env.example](.env.example)，所有配置项均可通过环境变量覆盖，无需改代码。

关键环境变量速览：

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `GUARD_SERVICE_API_KEY` | 空 | 鉴权密钥，空则不鉴权 |
| `OPENAI_API_KEY` | 空 | OpenAI API Key（审核+Embedding） |
| `EMBEDDING_API_KEY` | 空 | Embedding 专用 Key，优先于 OPENAI_API_KEY |
| `GUARD_STRICT_MODE` | true | Fail-Closed 模式 |
| `GUARD_DECAY_RATE` | 0.1 | 记忆衰减速率 |
| `GUARD_CONFLICT_SIMILARITY_THRESHOLD` | 0.75 | 冲突检测相似度阈值 |
| `GUARD_CONFLICT_PENALTY_WEIGHT` | 0.6 | 冲突降权惩罚权重 |
| `GUARD_AUDIT_FILE_PATH` | 空 | 审计日志落盘路径，空则仅存内存 |
