"""
安全护栏 FastAPI 服务 —— 供 CuteBlog (.NET) 通过 HTTP 调用。

返回格式对齐：
{
  "Success": true/false,
  "Message": "通过" / "失败原因",
  "Data": { ... },
  "Record": { "time": "...", "agentKey": "...", "operation": "...", "requestId": "..." }
}

启动方式：
  # 开发
  python start.py
  # 生产
  uvicorn guard_service:app --host 0.0.0.0 --port 8900 --workers 4

环境变量：
  见 .env.example
"""

from __future__ import annotations

import asyncio
import logging
import os
import time
import uuid
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from typing import Any, Optional, Union

from fastapi import FastAPI, Request, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field, ValidationError

from guard_algorithms.core import GuardAlgorithms
from guard_algorithms.config import GuardConfig
from guard_algorithms.defaults import (
    OpenAIModerationClient,
    RegexNerRedactor,
    OpenAIEmbedder,
    InMemoryHistoryProvider,
    JiebaEntityExtractor,
    InMemoryTrustLevelProvider,
    InMemoryMemoryStore,
    InMemoryAuditLogStore,
)
from guard_algorithms.local_embedder import LocalEmbedder
from guard_algorithms.memory_conflict import compute_conflicts_batch_sync

# ============================================================================
# 日志
# ============================================================================

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger("guard_service")


# ============================================================================
# 环境变量配置加载
# ============================================================================

def _env(key: str, default: str = "") -> str:
    return os.environ.get(key, default)


def _env_bool(key: str, default: bool = False) -> bool:
    val = os.environ.get(key, "").lower()
    if val in ("true", "1", "yes"):
        return True
    if val in ("false", "0", "no"):
        return False
    return default


def _env_int(key: str, default: int = 0) -> int:
    try:
        return int(os.environ.get(key, str(default)))
    except (ValueError, TypeError):
        return default


def _env_float(key: str, default: float = 0.0) -> float:
    try:
        return float(os.environ.get(key, str(default)))
    except (ValueError, TypeError):
        return default


def load_config_from_env() -> GuardConfig:
    """
    从环境变量加载 GuardConfig，未设置的走默认值。

    环境变量映射：
      GUARD_STRICT_MODE        → strict_mode
      GUARD_MAX_FIELD_LENGTH   → max_field_length
      GUARD_DECAY_RATE         → decay_rate
      GUARD_FORGET_THRESHOLD   → forget_threshold
      GUARD_MAX_CONSECUTIVE_DRIFT → max_consecutive_drift
      GUARD_STREAMING_SAMPLE_RATE → streaming_sample_rate
      GUARD_SAFE_RERANK_THRESHOLD → safe_rerank_threshold
      GUARD_AUDIT_SNIPPET_LENGTH  → audit_snippet_length
      GUARD_CONFLICT_SIMILARITY_THRESHOLD → conflict_similarity_threshold
      GUARD_CONFLICT_PENALTY_WEIGHT       → conflict_penalty_weight
    """
    return GuardConfig(
        strict_mode=_env_bool("GUARD_STRICT_MODE", True),
        max_field_length=_env_int("GUARD_MAX_FIELD_LENGTH", 100),
        decay_rate=_env_float("GUARD_DECAY_RATE", 0.1),
        forget_threshold=_env_float("GUARD_FORGET_THRESHOLD", 0.1),
        max_consecutive_drift=_env_int("GUARD_MAX_CONSECUTIVE_DRIFT", 3),
        streaming_sample_rate=_env_int("GUARD_STREAMING_SAMPLE_RATE", 3),
        safe_rerank_threshold=_env_float("GUARD_SAFE_RERANK_THRESHOLD", 0.2),
        audit_snippet_length=_env_int("GUARD_AUDIT_SNIPPET_LENGTH", 50),
        conflict_similarity_threshold=_env_float("GUARD_CONFLICT_SIMILARITY_THRESHOLD", 0.75),
        conflict_penalty_weight=_env_float("GUARD_CONFLICT_PENALTY_WEIGHT", 0.6),
    )


# ============================================================================
# 服务鉴权
# ============================================================================

SERVICE_API_KEY = _env("GUARD_SERVICE_API_KEY", "")
"""
服务级鉴权密钥。设置后，所有 /guard/* 请求必须携带 X-Guard-Key 头。
为空则不鉴权（仅限内网/开发环境使用）。
"""


def check_service_auth(request: Request) -> None:
    """校验服务级 API Key。未配置则跳过。"""
    if not SERVICE_API_KEY:
        return
    key = request.headers.get("X-Guard-Key", "")
    if key != SERVICE_API_KEY:
        raise HTTPException(status_code=401, detail="Unauthorized: invalid or missing X-Guard-Key")


# ============================================================================
# 应用生命周期
# ============================================================================

guard: Optional[GuardAlgorithms] = None
config: Optional[GuardConfig] = None

# 可插拔接口实例
_moderation: Optional[OpenAIModerationClient] = None
_ner: Optional[RegexNerRedactor] = None
_embedder: Optional[Union[OpenAIEmbedder, LocalEmbedder]] = None
_history: Optional[InMemoryHistoryProvider] = None
_entity: Optional[JiebaEntityExtractor] = None
_trust: Optional[InMemoryTrustLevelProvider] = None
_memory: Optional[InMemoryMemoryStore] = None
_audit: Optional[InMemoryAuditLogStore] = None

# 批量冲突检测队列：写入只入队，多 worker 并行消费
_conflict_queue: Optional[asyncio.Queue] = None
_conflict_worker_tasks: list[asyncio.Task] = []
CONFLICT_WORKER_COUNT = 3  # 并行 worker 数量


@asynccontextmanager
async def lifespan(app: FastAPI):
    """启动时初始化所有组件，关闭时清理资源。"""
    global guard, config, _moderation, _ner, _embedder, _history, _entity, _trust, _memory, _audit, _conflict_queue, _conflict_worker_tasks

    # 加载 .env 文件（如果存在）
    try:
        from dotenv import load_dotenv
        load_dotenv()
        logger.info("已加载 .env 文件")
    except ImportError:
        logger.debug("python-dotenv 未安装，跳过 .env 加载")

    # 加载配置
    config = load_config_from_env()
    logger.info(
        "配置加载完成：strict_mode=%s, max_field_length=%d, decay_rate=%.2f",
        config.strict_mode, config.max_field_length, config.decay_rate,
    )

    # 初始化算法引擎
    guard = GuardAlgorithms()

    # 初始化可插拔接口
    _moderation = OpenAIModerationClient()
    _ner = RegexNerRedactor()
    # 优先使用远程 Embedding API（无 GIL 问题），无 Key 时回退本地 fastembed
    emb_api_key = os.environ.get("EMBEDDING_API_KEY", "") or os.environ.get("OPENAI_API_KEY", "")
    if emb_api_key:
        _embedder = OpenAIEmbedder()
        logger.info("已加载远程 Embedder (model=%s, base=%s)", _embedder._model, _embedder._base_url)
    else:
        try:
            _embedder = LocalEmbedder()
            await _embedder.start()  # 启动子进程，隔离 GIL
            logger.info("已加载本地 Embedder (fastembed, 子进程隔离)")
        except Exception as e:
            logger.warning("本地 Embedder 加载失败: %s，回退到 OpenAI Embedder（无 Key 不可用）", e)
            _embedder = OpenAIEmbedder()
    _history = InMemoryHistoryProvider()
    _entity = JiebaEntityExtractor()
    _trust = InMemoryTrustLevelProvider()
    _memory = InMemoryMemoryStore()

    # 审计日志：可配置文件落盘路径
    audit_file = _env("GUARD_AUDIT_FILE_PATH", "")
    _audit = InMemoryAuditLogStore(file_path=audit_file if audit_file else None)

    # 预配置信任等级（从环境变量）
    _load_trust_levels(_trust)

    # 检查 numpy 可用性（冲突检测依赖 numpy 矩阵运算）
    try:
        import numpy as np
        logger.info("numpy 可用 (version=%s)", np.__version__)
    except ImportError:
        logger.warning("⚠️ numpy 不可用！冲突检测将回退纯 Python 模式（性能较差）")

    # 启动批量冲突检测 worker（多 worker 并行消费）
    _conflict_queue = asyncio.Queue()
    for i in range(CONFLICT_WORKER_COUNT):
        task = asyncio.create_task(_conflict_worker(worker_id=i))
        _conflict_worker_tasks.append(task)
    logger.info("CuteBlogGuard 服务启动完成 (pid=%d, conflict_workers=%d)", os.getpid(), CONFLICT_WORKER_COUNT)

    yield

    # 关闭时清理：停止 worker + 关闭 Embedder 子进程
    for task in _conflict_worker_tasks:
        task.cancel()
    _conflict_worker_tasks.clear()
    if _embedder and hasattr(_embedder, "close"):
        await _embedder.close()
    logger.info("CuteBlogGuard 服务关闭")


def _load_trust_levels(trust: InMemoryTrustLevelProvider) -> None:
    """
    从环境变量预加载信任等级。

    格式：GUARD_TRUST_LEVELS=key1:level1,key2:level2
    示例：GUARD_TRUST_LEVELS=sk-admin:admin,sk-user:user
    """
    raw = _env("GUARD_TRUST_LEVELS", "")
    if not raw:
        return
    for pair in raw.split(","):
        pair = pair.strip()
        if ":" not in pair:
            continue
        key, level = pair.split(":", 1)
        try:
            trust.set_level(key.strip(), level.strip())
            logger.info("预加载信任等级：%s → %s", key.strip(), level.strip())
        except ValueError as e:
            logger.warning("跳过无效信任等级：%s (%s)", pair, e)


async def _conflict_worker(worker_id: int = 0):
    """
    批量冲突检测后台 worker（多 worker 并行消费同一队列）。

    优化点：
    - 3 个 worker 并行消费，embedding 和冲突检测流水线化
    - 整批 compute_conflicts_batch_sync 一次线程池提交，减少调度开销
    - 一次性构建 source→entries 快照，避免逐条 list_by_source
    - 去掉 asyncio.sleep(0)，run_in_executor 本身让出事件循环
    """
    while True:
        try:
            # ── W1: 从队列取一批 ──
            t_w_start = time.perf_counter()
            item = await _conflict_queue.get()
            batch = [item]

            # 非阻塞多取（凑满一批 64 条）
            while len(batch) < 64:
                try:
                    extra = _conflict_queue.get_nowait()
                    batch.append(extra)
                except asyncio.QueueEmpty:
                    break
            t_w_dequeue = time.perf_counter()

            if not _embedder or not _memory or not config:
                continue

            # ── W2: 批量 embedding ──
            texts = [b["content"] for b in batch]
            try:
                embeddings = await _embedder.embed_batch(texts)
            except Exception as e:
                logger.warning("[W%d] 批量 embedding 失败 (%d 条): %s", worker_id, len(batch), e)
                continue
            t_w_embed = time.perf_counter()

            # ── W3: 批量冲突检测（一次线程池提交） ──
            # 1. 写入 embedding 到 entry
            # 2. 收集所有涉及的 source，一次性构建快照
            sources_in_batch: set[str] = set()
            batch_with_emb: list[dict] = []
            for b, embedding in zip(batch, embeddings):
                b["entry"].embedding = embedding
                sources_in_batch.add(b["source"])
                batch_with_emb.append({
                    "embedding": embedding,
                    "content_hash": b["content_hash"],
                    "source": b["source"],
                    "memory_id": b["memory_id"],
                })

            # 一次性构建 source → entries 快照（同步 dict 操作，极快）
            list_method = getattr(_memory, "list_by_source", None)
            if list_method is None:
                continue
            snapshot: dict[str, list] = {}
            for src in sources_in_batch:
                entries = list_method(src)
                if entries:
                    snapshot[src] = entries
                    # 调试日志：快照中有多少条目有 embedding
                    with_emb = sum(1 for e in entries if e.embedding is not None)
                    logger.info("[W%d] 📊 snapshot source=%s: total=%d, with_embedding=%d", worker_id, src, len(entries), with_emb)

            # 调试：记录本批条目的 memory_id（方便追踪锚点/冲突记忆）
            batch_ids = [b["memory_id"] for b in batch_with_emb]
            logger.info("[W%d] 📊 批次条目: %s", worker_id, batch_ids[:10])  # 只显示前10个

            # 一次线程池提交处理整批
            loop = asyncio.get_running_loop()
            all_conflicts = await loop.run_in_executor(
                None,
                compute_conflicts_batch_sync,
                batch_with_emb,
                snapshot,
                config.conflict_similarity_threshold,
                config.conflict_penalty_weight,
                config.forget_threshold,
            )
            t_w_conflict = time.perf_counter()

            # ── W4: 批量应用冲突结果（内存 dict 操作，极快） ──
            apply_errors = 0
            for c in all_conflicts:
                try:
                    if c["action"] == "archived":
                        await _memory.archive(c["memoryId"], "conflict_demoted_and_archived")
                        logger.info("[W%d] 📌 归档: %s sim=%.4f old=%.2f → archived", worker_id, c["memoryId"], c["similarity"], c["oldConfidence"])
                    else:
                        await _memory.update(c["memoryId"], c["newConfidence"])
                        logger.info("[W%d] 📌 降权: %s sim=%.4f %.2f → %.2f", worker_id, c["memoryId"], c["similarity"], c["oldConfidence"], c["newConfidence"])
                except Exception as e:
                    apply_errors += 1
                    logger.warning("[W%d] 📌 应用失败: %s error=%s", worker_id, c["memoryId"], e)

            conflict_count = len(all_conflicts)
            if conflict_count:
                # 按 source_memory_id 汇总日志
                by_source: dict[str, int] = {}
                for c in all_conflicts:
                    sid = c.get("source_memory_id", "?")
                    by_source[sid] = by_source.get(sid, 0) + 1
                for sid, cnt in by_source.items():
                    logger.info("[W%d] 冲突检测: %s → %d 条冲突", worker_id, sid, cnt)

            # ── HOOK 计时汇总 ──
            logger.info(
                "⏱ HOOK [conflict_worker W%d] batch=%d: wait=%.2fms embed=%.2fms conflict=%.2fms apply=%.2fms total=%.2fms conflicts=%d",
                worker_id,
                len(batch),
                (t_w_dequeue - t_w_start) * 1000,
                (t_w_embed - t_w_dequeue) * 1000,
                (t_w_conflict - t_w_embed) * 1000,
                (time.perf_counter() - t_w_conflict) * 1000,
                (time.perf_counter() - t_w_start) * 1000,
                conflict_count,
            )

        except asyncio.CancelledError:
            logger.info("[W%d] 冲突检测 worker 已停止", worker_id)
            break
        except Exception as e:
            logger.warning("[W%d] 冲突检测 worker 异常: %s", worker_id, e)
            await asyncio.sleep(1)


# ============================================================================
# FastAPI 应用
# ============================================================================

app = FastAPI(
    title="CuteBlogGuard",
    version="2.0.0",
    description="安全护栏微服务 —— 输入检测/工具校验/会话验证/漂移检测/流式拦截/审计/记忆/RAG重排/信任/衰减",
    lifespan=lifespan,
)

# CORS —— 允许跨域调用
app.add_middleware(
    CORSMiddleware,
    allow_origins=_env("GUARD_CORS_ORIGINS", "*").split(","),
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)


# ── 全局计时中间件：卡住完整请求生命周期 ──
@app.middleware("http")
async def timing_middleware(request: Request, call_next):
    t0 = time.perf_counter()
    response = await call_next(request)
    t1 = time.perf_counter()
    logger.info(
        "⏱ HOOK [middleware] %s %s: total=%.2fms",
        request.method, request.url.path, (t1 - t0) * 1000,
    )
    return response


# ============================================================================
# 全局异常处理 —— 任何错误都返回 GuardResult 格式
# ============================================================================

class GuardResult(BaseModel):
    """统一返回结构"""
    Success: bool
    Message: str
    Data: Optional[Any] = None
    Record: Optional[Any] = None


def _error_result(message: str, request_id: str = "") -> GuardResult:
    return GuardResult(
        Success=False,
        Message=message,
        Data={
            "requestId": request_id or uuid.uuid4().hex[:16],
            "blocked": True,
            "riskLabel": "internal_error",
        },
        Record=_make_record(request_id or uuid.uuid4().hex[:16], "", "error"),
    )


@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception):
    """全局异常兜底 —— 确保永远返回 GuardResult 格式。"""
    logger.exception("未捕获异常: %s %s → %s", request.method, request.url.path, exc)
    return JSONResponse(
        status_code=200,  # 始终 200，通过 Success=false 表达失败
        content=_error_result(f"服务内部错误: {type(exc).__name__}").model_dump(),
    )


@app.exception_handler(HTTPException)
async def http_exception_handler(request: Request, exc: HTTPException):
    """HTTP 异常（如鉴权失败）也返回 GuardResult 格式。"""
    if exc.status_code == 401:
        return JSONResponse(
            status_code=401,
            content={
                "Success": False,
                "Message": "鉴权失败：缺少或无效的 X-Guard-Key",
                "Data": {"blocked": True, "riskLabel": "unauthorized"},
                "Record": None,
            },
        )
    return JSONResponse(
        status_code=exc.status_code,
        content=_error_result(exc.detail).model_dump(),
    )


@app.exception_handler(ValidationError)
async def validation_exception_handler(request: Request, exc: ValidationError):
    """Pydantic 请求体验证错误 → GuardResult。"""
    logger.warning("请求体验证失败: %s %s → %s", request.method, request.url.path, exc)
    return JSONResponse(
        status_code=200,
        content=_error_result(f"请求参数无效: {exc.errors()[0]['msg'] if exc.errors() else 'unknown'}").model_dump(),
    )


# 处理 FastAPI 内部 JSON 解析失败（RequestValidationError）
from fastapi.exceptions import RequestValidationError


@app.exception_handler(RequestValidationError)
async def request_validation_exception_handler(request: Request, exc: RequestValidationError):
    """请求体验证错误（JSON格式错误/字段缺失）→ GuardResult 格式。"""
    logger.warning("请求格式错误: %s %s → %s", request.method, request.url.path, exc)
    first_error = exc.errors()[0] if exc.errors() else {}
    loc = " → ".join(str(l) for l in first_error.get("loc", []))
    msg = first_error.get("msg", "请求格式无效")
    return JSONResponse(
        status_code=200,
        content=_error_result(f"请求格式错误 [{loc}]: {msg}").model_dump(),
    )


# ============================================================================
# 请求/响应模型
# ============================================================================

class GuardRecord(BaseModel):
    """审计记录"""
    time: str = Field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    agentKey: str = ""
    operation: str = ""
    requestId: str = ""


class GuardData(BaseModel):
    """返回数据"""
    requestId: str = ""
    blocked: bool = False
    riskLabel: Optional[str] = None
    details: Optional[dict[str, Any]] = None


def _new_request_id() -> str:
    return uuid.uuid4().hex[:16]


def _make_record(request_id: str, agent_key: str, operation: str) -> GuardRecord:
    return GuardRecord(
        time=datetime.now(timezone.utc).isoformat(),
        agentKey=agent_key,
        operation=operation,
        requestId=request_id,
    )


def _parse_guard_json(raw_json: str) -> dict:
    """把 GuardAlgorithms 返回的 JSON 字符串解析为 dict"""
    import json
    return json.loads(raw_json)


def _to_guard_result(raw_json: str, agent_key: str, operation: str) -> GuardResult:
    """把内部 JSON 响应转换为 CuteBlog 要求的 GuardResult 格式"""
    import json
    d = json.loads(raw_json)

    success = d.get("success", False)
    message = d.get("message", "")
    data = d.get("data", {})
    request_id = data.get("requestId", _new_request_id())

    # 提取风险标签
    risk_label = data.get("riskLabel")
    blocked = data.get("blocked", not success)
    details = data.get("details", {})

    # message 翻译：内部码 → 人类可读
    msg_map = {
        "ok": "通过",
        "missing_input": "输入为空",
        "invalid_body": "请求体无效",
        "blocked_pii_error": "PII脱敏异常，严格模式阻断",
        "blocked_missing_moderation": "规则命中且无审核服务，严格模式阻断",
        "blocked_moderation_error": "审核服务异常，严格模式阻断",
        "blocked": "语义审核判定不安全",
        "blocked_unknown_tool": "未知工具，拒绝调用",
        "blocked_invalid_args": "参数格式无效",
        "blocked_args_validation": "参数校验失败",
        "blocked_injection": "参数中检测到注入特征",
        "blocked_missing_content": "内容为空",
        "blocked_missing_query": "查询为空",
        "blocked_missing_session": "会话ID为空",
        "missing_client_history": "客户端历史为空",
        "blocked_tampering": "聊天历史被篡改",
        "blocked_topic_drift": "检测到主题漂移",
        "blocked_privilege_escalation": "检测到提权攻击",
        "blocked_no_trust_provider": "无信任等级提供者",
        "blocked_trust_provider_error": "信任等级查询异常",
        "blocked_streaming": "流式输出包含危险内容",
        "blocked_streaming_error": "流式审核异常，严格模式阻断",
        "blocked_no_store": "无记忆存储",
        "blocked_memory_not_found": "记忆条目不存在",
        "blocked_no_audit_store": "无审计日志存储",
        "blocked_audit_error": "审计写入异常",
        "blocked_store_error": "存储操作异常",
        "blocked_missing_embedder": "无Embedding服务",
        "missing_event_type": "审计事件类型为空",
        "missing_query": "查询为空",
        "missing_segments": "片段为空",
        "missing_api_key": "API Key 为空",
        "missing_memory_id": "记忆ID为空",
        "missing_buffer": "缓冲区为空",
        "archived": "记忆已归档",
    }
    human_msg = msg_map.get(message, message if message else ("通过" if success else "安全检测未通过"))

    result = GuardResult(
        Success=success,
        Message=human_msg,
        Data=GuardData(
            requestId=request_id,
            blocked=blocked,
            riskLabel=risk_label,
            details=details,
        ),
        Record=_make_record(request_id, agent_key, operation),
    )

    # ---- 自动审计：拦截或检测到风险时自动写日志 ----
    if blocked or risk_label:
        _auto_audit(
            request_id=request_id,
            operation=operation,
            message=message,
            risk_label=risk_label,
            details=details,
        )

    return result


def _auto_audit(
    request_id: str,
    operation: str,
    message: str,
    risk_label: Optional[str],
    details: dict,
) -> None:
    """自动写审计日志（仅拦截/有风险标签时触发，集成方无需手动调用）"""
    if not _audit:
        return
    try:
        snippet = str(details)[:config.audit_snippet_length]
        import hashlib
        content_hash = hashlib.sha256(snippet.encode()).hexdigest()
        _audit.append({
            "requestId": request_id,
            "eventType": risk_label or message,
            "contentHash": content_hash,
            "snippet": snippet,
            "operation": operation,
            "timestamp": datetime.now(timezone.utc).isoformat(),
        })
        logger.info(f"自动审计: operation={operation}, risk={risk_label}, requestId={request_id}")
    except Exception as e:
        logger.warning(f"自动审计写入失败: {e}")


# ============================================================================
# 请求体模型
# ============================================================================

class InputGuardRequest(BaseModel):
    """输入检测请求"""
    input: str = Field(..., description="用户输入文本")
    agentKey: str = Field(default="ai-chat", description="Agent 标识")
    strictMode: bool = Field(default=True, description="严格模式")


class ToolCallGuardRequest(BaseModel):
    """工具调用检测请求"""
    tool: str = Field(..., description="工具名")
    args: dict[str, Any] = Field(default_factory=dict, description="工具参数")
    allowedTools: list[str] = Field(default_factory=list, description="允许的工具名白名单")
    agentKey: str = Field(default="ai-agent", description="Agent 标识")
    strictMode: bool = Field(default=True, description="严格模式")


class ContentGuardRequest(BaseModel):
    """用户内容审核请求（评论/留言等）"""
    content: str = Field(..., description="用户内容")
    agentKey: str = Field(default="comment-audit", description="Agent 标识")
    strictMode: bool = Field(default=True, description="严格模式")


class SessionIntegrityRequest(BaseModel):
    """会话完整性校验请求"""
    sessionId: str = Field(..., description="会话ID")
    clientHistory: list[dict[str, str]] = Field(
        default_factory=list,
        description="客户端历史 [{id, content}]",
    )
    agentKey: str = Field(default="ai-chat", description="Agent 标识")


class TopicDriftRequest(BaseModel):
    """主题漂移检测请求"""
    query: str = Field(..., description="原始查询")
    segments: list[str] = Field(default_factory=list, description="后续片段列表")
    agentKey: str = Field(default="ai-chat", description="Agent 标识")


class StreamingGuardRequest(BaseModel):
    """流式输出检测请求"""
    buffer: str = Field(..., description="当前缓冲区内容")
    segmentIndex: int = Field(default=0, description="当前段索引")
    riskLevel: str = Field(default="low", description="风险等级: low | high")
    agentKey: str = Field(default="ai-chat", description="Agent 标识")
    strictMode: bool = Field(default=True, description="严格模式")



class MemoryWriteRequest(BaseModel):
    """记忆写入安全网关请求"""
    content: str = Field(..., description="要写入记忆的文本")
    source: str = Field(default="user", description="来源：user|admin|system")
    ttlSeconds: int = Field(default=86400, description="TTL 秒数")
    agentKey: str = Field(default="memory-writer", description="Agent 标识")
    strictMode: bool = Field(default=True, description="严格模式")


class RagRerankRequest(BaseModel):
    """RAG 安全重排请求"""
    query: str = Field(..., description="用户问题")
    candidates: list[dict[str, Any]] = Field(
        default_factory=list,
        description='候选文档 [{"id","content","embedding","source"}]',
    )
    agentKey: str = Field(default="rag-service", description="Agent 标识")


class SemanticCompleteRequest(BaseModel):
    """语义完整性判断请求"""
    buffer: str = Field(..., description="当前累积的流式文本")
    maxBufferLen: int = Field(default=500, description="最大缓冲长度")
    agentKey: str = Field(default="streaming", description="Agent 标识")


class TrustLevelRequest(BaseModel):
    """信任等级解析请求"""
    apiKey: str = Field(..., description="API Key 或会话标识")
    userInput: str = Field(default="", description="用户输入（用于提权检测）")
    agentKey: str = Field(default="auth", description="Agent 标识")


class MemoryDecayRequest(BaseModel):
    """记忆衰减请求"""
    memoryId: str = Field(..., description="记忆ID")
    agentKey: str = Field(default="decay-scheduler", description="Agent 标识")


# ---- 管理型请求体 ----

class SetSessionHistoryRequest(BaseModel):
    """设置会话历史请求"""
    sessionId: str = Field(..., description="会话ID")
    history: list[dict[str, str]] = Field(
        ...,
        description='服务端权威历史 [{"id":"msg-1","content":"你好"}, ...]',
    )
    agentKey: str = Field(default="session-manager", description="Agent 标识")


class SetTrustLevelRequest(BaseModel):
    """设置信任等级请求"""
    apiKey: str = Field(..., description="API Key")
    level: str = Field(..., description="信任等级：system | admin | user | untrusted")
    agentKey: str = Field(default="auth-admin", description="Agent 标识")


class PutMemoryRequest(BaseModel):
    """存入记忆条目请求"""
    memoryId: str = Field(..., description="记忆ID")
    content: str = Field(..., description="记忆内容")
    confidence: float = Field(default=1.0, description="初始置信度 (0~1)")
    source: str = Field(default="user", description="来源标签")
    lastPositiveRef: str = Field(default="", description="最后正向引用时间 (ISO8601)，空=无引用")
    agentKey: str = Field(default="memory-manager", description="Agent 标识")


class GetAuditLogsRequest(BaseModel):
    """查询审计日志请求"""
    limit: int = Field(default=100, description="最多返回条数")
    offset: int = Field(default=0, description="跳过条数")


# ============================================================================
# API 端点 —— 检测类（12个 + 健康检查）
# ============================================================================

@app.post("/guard/input", response_model=GuardResult, summary="输入侧安全检测")
async def guard_input(req: InputGuardRequest, request: Request):
    """
    用户消息进入 AI 前的安全检测。
    流程：编码归一化 → PII 脱敏 → 规则快筛 → 语义审核(可选)
    """
    check_service_auth(request)
    c = GuardConfig(strict_mode=req.strictMode) if not req.strictMode else config
    raw = await guard.guard_input({"input": req.input}, c, _moderation, _ner)
    return _to_guard_result(raw, req.agentKey, "guard_input")


@app.post("/guard/tool-call", response_model=GuardResult, summary="工具调用参数校验")
async def guard_tool_call(req: ToolCallGuardRequest, request: Request):
    """
    Semantic Kernel 工具调用前的安全校验。
    流程：工具名白名单 → 参数格式校验 → 参数长度/字符集校验 → 注入扫描
    """
    check_service_auth(request)
    c = GuardConfig(strict_mode=req.strictMode) if not req.strictMode else config
    allowed = dict.fromkeys(req.allowedTools, True)
    raw = await guard.guard_tool_call(
        {"tool": req.tool, "args": req.args},
        c, allowed)
    return _to_guard_result(raw, req.agentKey, "guard_tool_call")


@app.post("/guard/content", response_model=GuardResult, summary="用户内容审核")
async def guard_content(req: ContentGuardRequest, request: Request):
    """
    评论/留言等内容审核（替代 CommentAuditHelper）。
    流程：编码归一化 → PII 脱敏 → 规则快筛
    """
    check_service_auth(request)
    c = GuardConfig(strict_mode=req.strictMode) if not req.strictMode else config
    raw = await guard.guard_input({"input": req.content}, c, _moderation, _ner)
    return _to_guard_result(raw, req.agentKey, "guard_content")


@app.post("/guard/session-integrity", response_model=GuardResult, summary="会话完整性校验")
async def guard_session_integrity(req: SessionIntegrityRequest, request: Request):
    """
    验证客户端提交的聊天历史是否与服务端一致。
    流程：客户端提交 {id, content} → 服务端查权威历史 → SHA256 比对
    """
    check_service_auth(request)
    raw = await guard.verify_session_integrity(
        {
            "sessionId": req.sessionId,
            "clientHistory": [h for h in req.clientHistory],
        },
        _history,
    )
    return _to_guard_result(raw, req.agentKey, "verify_session")


@app.post("/guard/topic-drift", response_model=GuardResult, summary="主题漂移检测")
async def guard_topic_drift(req: TopicDriftRequest, request: Request):
    """
    检测多轮对话中话题是否被恶意引导偏移。
    """
    check_service_auth(request)
    raw = await guard.check_topic_drift(
        {"query": req.query, "segments": req.segments}, config, _entity)
    return _to_guard_result(raw, req.agentKey, "check_drift")


@app.post("/guard/streaming", response_model=GuardResult, summary="流式输出安全拦截")
async def guard_streaming(req: StreamingGuardRequest, request: Request):
    """
    流式输出逐段安全检测。
    """
    check_service_auth(request)
    c = GuardConfig(strict_mode=req.strictMode) if not req.strictMode else config
    raw = await guard.guard_streaming_output(
        {"buffer": req.buffer, "segmentIndex": req.segmentIndex, "riskLevel": req.riskLevel},
        c, _moderation)
    return _to_guard_result(raw, req.agentKey, "guard_streaming")


@app.post("/guard/memory-write", response_model=GuardResult, summary="记忆写入安全网关")
async def guard_memory_write(req: MemoryWriteRequest, request: Request):
    """
    对写入长期记忆的内容执行与输入侧同等强度的安全检测。
    流程：编码归一化 → PII 脱敏 → 规则快筛 → 语义审核(可选)
    本方法只返回"是否允许写入"，不实际写入存储。
    """
    check_service_auth(request)
    c = GuardConfig(strict_mode=req.strictMode) if not req.strictMode else config
    raw = await guard.guard_memory_write(
        {"content": req.content, "source": req.source, "ttlSeconds": req.ttlSeconds},
        c, _moderation, _ner)
    return _to_guard_result(raw, req.agentKey, "guard_memory_write")


@app.post("/guard/rag-rerank", response_model=GuardResult, summary="RAG 安全重排")
async def guard_rag_rerank(req: RagRerankRequest, request: Request):
    """
    对召回的候选文档进行安全过滤和信任度加权重排。
    流程：注入特征扫描 → 相关性计算 → 信任度加权 → 阈值过滤
    ⚠️ doc embedding 必须在入库时预计算，不在查询时重复计算。
    """
    check_service_auth(request)
    raw = await guard.guard_rag_rerank(
        {"query": req.query, "candidates": req.candidates},
        config, _embedder)
    return _to_guard_result(raw, req.agentKey, "guard_rag_rerank")


@app.post("/guard/semantic-complete", response_model=GuardResult, summary="语义完整性判断")
async def guard_semantic_complete(req: SemanticCompleteRequest, request: Request):
    """
    判断流式缓冲区是否形成了完整的语义单元。
    判断逻辑：强断句标点结尾 → 完整；引号内伪终止 → 不完整；超长 → 强制完整
    """
    check_service_auth(request)
    raw = await guard.is_semantic_complete(
        {"buffer": req.buffer, "maxBufferLen": req.maxBufferLen})
    return _to_guard_result(raw, req.agentKey, "is_semantic_complete")


@app.post("/guard/trust-level", response_model=GuardResult, summary="信任等级解析")
async def guard_trust_level(req: TrustLevelRequest, request: Request):
    """
    来源隔离与信任分级 —— 服务端权威校验信任等级 + 提权攻击识别。
    流程：API Key → 服务端查信任等级 → 提权模式扫描 → 返回等级/降级
    """
    check_service_auth(request)
    raw = await guard.resolve_trust_level(
        {"apiKey": req.apiKey, "userInput": req.userInput},
        config, _trust)
    return _to_guard_result(raw, req.agentKey, "resolve_trust_level")


@app.post("/guard/memory-decay", response_model=GuardResult, summary="记忆衰减")
async def guard_memory_decay(req: MemoryDecayRequest, request: Request):
    """
    记忆衰减 —— 基于正向引用的指数衰减，低置信度自动归档。
    ⚠️ 应在定时任务中批量调用，不应在主请求链路中同步执行。
    """
    check_service_auth(request)
    raw = await guard.update_memory_decay(
        {"memoryId": req.memoryId}, config, _memory)
    return _to_guard_result(raw, req.agentKey, "update_memory_decay")


# ============================================================================
# 运维端点
# ============================================================================

@app.get("/health", summary="健康检查")
async def health():
    """
    健康检查 —— 返回服务状态和关键依赖可用性。

    集成方可据此判断服务是否就绪。
    """
    deps = {
        "moderation": _moderation is not None and bool(_moderation._api_key),
        "embedder": _embedder is not None and (hasattr(_embedder, 'is_ready') and _embedder.is_ready() or bool(getattr(_embedder, '_api_key', ''))),
        "history": _history is not None,
        "entity": _entity is not None,
        "trust": _trust is not None,
        "memory": _memory is not None,
        "audit": _audit is not None,
    }
    all_ok = all(deps.values()) or True  # 内存实现始终可用
    return {
        "status": "ok" if all_ok else "degraded",
        "service": "CuteBlogGuard",
        "version": "2.0.0",
        "strictMode": config.strict_mode if config else True,
        "dependencies": deps,
    }


@app.get("/guard/config", summary="查看当前配置（只读）")
async def get_config(request: Request):
    """
    返回当前生效的运行时配置（只读，不含敏感信息）。

    集成方可用于确认服务端配置是否正确。
    """
    check_service_auth(request)
    if not config:
        return {"Success": False, "Message": "服务未初始化"}
    return {
        "Success": True,
        "Message": "通过",
        "Data": {
            "strictMode": config.strict_mode,
            "maxFieldLength": config.max_field_length,
            "safeCharsetPattern": config.safe_charset_pattern,
            "maxDecodeRounds": config.max_decode_rounds,
            "maxDecodedBytes": config.max_decoded_bytes,
            "dangerousPatternsCount": len(config.dangerous_patterns),
            "piiPatternsCount": len(config.pii_patterns),
            "safeRerankThreshold": config.safe_rerank_threshold,
            "maxConsecutiveDrift": config.max_consecutive_drift,
            "decayRate": config.decay_rate,
            "forgetThreshold": config.forget_threshold,
            "streamingSampleRate": config.streaming_sample_rate,
            "auditSnippetLength": config.audit_snippet_length,
            "escalationPatternsCount": len(config.privilege_escalation_patterns),
            "conflictSimilarityThreshold": config.conflict_similarity_threshold,
            "conflictPenaltyWeight": config.conflict_penalty_weight,
        },
    }


@app.get("/guard/audit/stats", summary="审计日志统计")
async def audit_stats(request: Request):
    """
    返回审计日志条目数（调试/运维用）。
    """
    check_service_auth(request)
    count = _audit.count() if _audit else 0
    return {
        "Success": True,
        "Message": "通过",
        "Data": {"totalEntries": count},
    }


# ============================================================================
# 管理型端点 —— 集成方通过 HTTP 管理后台数据，无需写 Python 代码
# ============================================================================

@app.post("/manage/session/set-history", summary="设置会话历史（服务端权威数据）")
async def manage_set_session_history(req: SetSessionHistoryRequest, request: Request):
    """
    设置指定会话的服务端权威历史。

    集成方在每次对话结束后调用此接口，将服务端记录的消息同步到护栏服务，
    后续 verify_session_integrity 会用这些数据校验客户端提交的历史是否被篡改。

    请求体：
      {
        "sessionId": "sess-001",
        "history": [
          {"id": "msg-1", "content": "你好"},
          {"id": "msg-2", "content": "你好！有什么可以帮你的？"}
        ]
      }

    注意：content 会在服务端自动计算 SHA256 哈希，集成方只需传原始内容。
    """
    check_service_auth(request)

    if not _history:
        return {"Success": False, "Message": "历史存储未初始化"}

    # 将 {id, content} 转为 {id, sha256(content)} 格式存储
    import hashlib
    server_history = []
    for item in req.history:
        mid = item.get("id", "")
        content = item.get("content", "")
        if mid:
            content_hash = hashlib.sha256(content.encode("utf-8")).hexdigest()
            server_history.append((mid, content_hash))

    _history.set_history(req.sessionId, server_history)
    logger.info("会话历史已设置: %s (%d 条)", req.sessionId, len(server_history))

    return {
        "Success": True,
        "Message": "通过",
        "Data": {
            "sessionId": req.sessionId,
            "entryCount": len(server_history),
        },
    }


@app.delete("/manage/session/{session_id}", summary="清除会话历史")
async def manage_delete_session_history(session_id: str, request: Request):
    """
    清除指定会话的服务端历史。会话结束后建议调用，避免内存泄漏。
    """
    check_service_auth(request)

    if not _history:
        return {"Success": False, "Message": "历史存储未初始化"}

    _history.set_history(session_id, [])
    logger.info("会话历史已清除: %s", session_id)

    return {
        "Success": True,
        "Message": "通过",
        "Data": {"sessionId": session_id, "action": "cleared"},
    }


@app.post("/manage/trust/set-level", summary="设置信任等级")
async def manage_set_trust_level(req: SetTrustLevelRequest, request: Request):
    """
    设置 API Key 对应的信任等级。

    集成方在用户鉴权完成后调用此接口，将用户的真实权限等级同步到护栏服务，
    后续 resolve_trust_level 会用这些数据校验请求是否合法。

    信任等级（从高到低）：
      system → admin → user → untrusted

    也可以通过环境变量 GUARD_TRUST_LEVELS 预加载（服务启动时一次性设置），
    本接口用于运行时动态更新。
    """
    check_service_auth(request)

    if not _trust:
        return {"Success": False, "Message": "信任等级存储未初始化"}

    try:
        _trust.set_level(req.apiKey, req.level)
        logger.info("信任等级已设置: %s → %s", req.apiKey[:8] + "...", req.level)
    except ValueError as e:
        return {"Success": False, "Message": f"无效信任等级: {e}"}

    return {
        "Success": True,
        "Message": "通过",
        "Data": {"apiKey": req.apiKey[:8] + "...", "level": req.level},
    }


@app.delete("/manage/trust/{api_key}", summary="删除信任等级")
async def manage_delete_trust_level(api_key: str, request: Request):
    """
    删除指定 API Key 的信任等级（如用户注销/Token过期时调用）。
    删除后该 Key 将回退到 untrusted。
    """
    check_service_auth(request)

    if not _trust:
        return {"Success": False, "Message": "信任等级存储未初始化"}

    if api_key in _trust._store:
        del _trust._store[api_key]
        logger.info("信任等级已删除: %s", api_key[:8] + "...")

    return {
        "Success": True,
        "Message": "通过",
        "Data": {"apiKey": api_key[:8] + "...", "action": "deleted"},
    }


@app.post("/manage/memory/put", summary="存入记忆条目")
async def manage_put_memory(req: PutMemoryRequest, request: Request):
    """
    存入一条记忆条目。

    集成方在记忆写入安全检测通过后调用此接口，将脱敏后的记忆存入护栏服务，
    后续 update_memory_decay 会基于这些数据执行衰减计算。

    请求体：
      {
        "memoryId": "mem-001",
        "content": "用户偏好：喜欢简洁的回答风格",
        "confidence": 1.0,
        "source": "user_profile",
        "lastPositiveRef": ""   // 空字符串=无引用，或传 ISO8601 时间
      }

    注意：content 应使用 guard_memory_write 返回的脱敏后内容，不要传原始输入。
    """
    check_service_auth(request)

    if not _memory:
        return {"Success": False, "Message": "记忆存储未初始化"}

    t_handler_start = time.perf_counter()

    # ── HOOK 1: 解析 lastPositiveRef ──
    last_ref = None
    if req.lastPositiveRef:
        try:
            from datetime import datetime as _dt
            last_ref = _dt.fromisoformat(req.lastPositiveRef)
        except (ValueError, TypeError):
            return {"Success": False, "Message": "lastPositiveRef 格式无效，需要 ISO8601"}
    t_parse = time.perf_counter()

    # ── HOOK 2: 构造 MemoryEntry ──
    from guard_algorithms.models import MemoryEntry
    entry = MemoryEntry(
        id=req.memoryId,
        content=req.content,
        confidence=req.confidence,
        last_positive_ref=last_ref,
        source=req.source,
    )
    t_construct = time.perf_counter()

    # ── HOOK 3: 写入 _memory.put() ──
    _memory.put(req.memoryId, entry)
    t_put = time.perf_counter()

    # ── HOOK 4: 入队冲突检测 ──
    if _conflict_queue is not None and _embedder is not None:
        _conflict_queue.put_nowait({
            "memory_id": req.memoryId,
            "content": req.content,
            "source": req.source,
            "content_hash": guard._sha256(req.content),
            "entry": entry,
        })
    t_enqueue = time.perf_counter()

    # ── 计时汇总 ──
    logger.info(
        "⏱ HOOK [put_memory] %s: parse=%.2fms construct=%.2fms put=%.2fms enqueue=%.2fms total=%.2fms",
        req.memoryId,
        (t_parse - t_handler_start) * 1000,
        (t_construct - t_parse) * 1000,
        (t_put - t_construct) * 1000,
        (t_enqueue - t_put) * 1000,
        (t_enqueue - t_handler_start) * 1000,
    )

    return {
        "Success": True,
        "Message": "通过",
        "Data": {
            "memoryId": req.memoryId,
            "confidence": req.confidence,
            "source": req.source,
            "conflictCheck": "queued",
        },
    }


class BulkMemoryItem(BaseModel):
    """批量初始化中的单条记忆"""
    memoryId: str = Field(..., description="记忆ID")
    content: str = Field(..., description="记忆内容")
    confidence: float = Field(default=1.0, description="初始置信度 (0~1)")
    source: str = Field(default="user", description="来源标签")
    lastPositiveRef: str = Field(default="", description="最后正向引用时间 (ISO8601)")


class BulkMemoryRequest(BaseModel):
    """批量初始化记忆请求"""
    memories: list[BulkMemoryItem] = Field(..., description="记忆条目列表，最多500条")
    skipConflictCheck: bool = Field(default=False, description="跳过冲突检测（纯导入）")


@app.post("/manage/memory/bulk", summary="批量初始化记忆（两阶段处理）")
async def manage_bulk_memory(req: BulkMemoryRequest, request: Request):
    """
    批量初始化记忆条目 —— 两阶段处理，解决逐条写入时冲突检测失效的问题。

    问题背景：
      逐条 PUT /manage/memory/put 时，后台 conflict_worker 按批处理。
      处理第 N 条时，第 N+1 条尚未被 embed，导致 compute_conflicts_batch_sync
      跳过 embedding=None 的条目，冲突检测几乎失效。

    解决方案（两阶段）：
      阶段1 — 全部写入 + 批量 embed：将所有条目写入存储，然后一次性
               embed_batch 计算 embedding 并缓存到 entry 上。
      阶段2 — 统一冲突检测：所有条目都有了 embedding 后，按 source 分组
               一次性做冲突检测，确保同 source 的条目之间能互相比对到。

    请求体：
      {
        "memories": [
          {"memoryId": "mem-001", "content": "...", "confidence": 0.9, "source": "user"},
          {"memoryId": "mem-002", "content": "...", "confidence": 1.0, "source": "user"},
          ...
        ],
        "skipConflictCheck": false
      }

    限制：单次最多 500 条。更多请分批调用。

    返回：
      {
        "Success": true,
        "Data": {
          "total": 100,
          "embedded": 100,
          "conflicts": 12,
          "phase1_ms": 523,
          "phase2_ms": 89
        }
      }
    """
    check_service_auth(request)

    if not _memory or not _embedder or not config:
        return {"Success": False, "Message": "服务未初始化"}

    items = req.memories
    if len(items) > 500:
        return {"Success": False, "Message": f"单次最多 500 条，当前 {len(items)} 条"}

    t_total_start = time.perf_counter()

    # ══════════════════════════════════════════════════
    # 阶段1：全部写入 + 批量 embed
    # ══════════════════════════════════════════════════
    from guard_algorithms.models import MemoryEntry

    entries: list[MemoryEntry] = []
    write_count = 0
    write_errors = 0

    for item in items:
        last_ref = None
        if item.lastPositiveRef:
            try:
                from datetime import datetime as _dt
                last_ref = _dt.fromisoformat(item.lastPositiveRef)
            except (ValueError, TypeError):
                write_errors += 1
                continue

        entry = MemoryEntry(
            id=item.memoryId,
            content=item.content,
            confidence=item.confidence,
            last_positive_ref=last_ref,
            source=item.source,
        )
        _memory.put(item.memoryId, entry)
        entries.append(entry)
        write_count += 1

    # 批量 embed（64 条一批，复用 embed_batch）
    embed_count = 0
    embed_errors = 0
    embed_batch_size = 64

    for i in range(0, len(entries), embed_batch_size):
        batch_entries = entries[i:i + embed_batch_size]
        texts = [e.content for e in batch_entries]
        try:
            embeddings = await _embedder.embed_batch(texts)
            for entry, embedding in zip(batch_entries, embeddings):
                entry.embedding = embedding
                embed_count += 1
        except Exception as e:
            logger.warning("批量 embed 失败 (offset=%d): %s", i, e)
            embed_errors += len(batch_entries)

    t_phase1 = time.perf_counter()
    phase1_ms = (t_phase1 - t_total_start) * 1000

    # ══════════════════════════════════════════════════
    # 阶段2：统一冲突检测
    # ══════════════════════════════════════════════════
    conflict_count = 0

    if not req.skipConflictCheck and embed_count > 0:
        # 收集所有 source，构建快照
        sources: set[str] = set(e.source for e in entries if e.embedding is not None)
        list_method = getattr(_memory, "list_by_source", None)
        if list_method is not None:
            snapshot: dict[str, list] = {}
            for src in sources:
                src_entries = list_method(src)
                if src_entries:
                    snapshot[src] = src_entries

            # 构建冲突检测输入（只有成功 embed 的条目）
            batch_items = []
            for entry in entries:
                if entry.embedding is None:
                    continue
                batch_items.append({
                    "embedding": entry.embedding,
                    "content_hash": guard._sha256(entry.content),
                    "source": entry.source,
                    "memory_id": entry.id,
                })

            if batch_items:
                # 一次线程池提交
                loop = asyncio.get_running_loop()
                all_conflicts = await loop.run_in_executor(
                    None,
                    compute_conflicts_batch_sync,
                    batch_items,
                    snapshot,
                    config.conflict_similarity_threshold,
                    config.conflict_penalty_weight,
                    config.forget_threshold,
                )

                # 应用冲突结果
                for c in all_conflicts:
                    try:
                        if c["action"] == "archived":
                            await _memory.archive(c["memoryId"], "conflict_demoted_and_archived")
                            logger.info("📌 [bulk] 归档: %s sim=%.4f old=%.2f → archived", c["memoryId"], c["similarity"], c["oldConfidence"])
                        else:
                            await _memory.update(c["memoryId"], c["newConfidence"])
                            logger.info("📌 [bulk] 降权: %s sim=%.4f %.2f → %.2f", c["memoryId"], c["similarity"], c["oldConfidence"], c["newConfidence"])
                    except Exception as e:
                        logger.warning("📌 [bulk] 应用失败: %s error=%s", c["memoryId"], e)

                conflict_count = len(all_conflicts)

    t_phase2 = time.perf_counter()
    phase2_ms = (t_phase2 - t_phase1) * 1000

    logger.info(
        "批量初始化: 写入=%d embed=%d(+%d失败) 冲突=%d 阶段1=%.1fms 阶段2=%.1fms",
        write_count, embed_count, embed_errors, conflict_count, phase1_ms, phase2_ms,
    )

    return {
        "Success": True,
        "Message": "通过",
        "Data": {
            "total": write_count,
            "embedded": embed_count,
            "embedErrors": embed_errors,
            "writeErrors": write_errors,
            "conflicts": conflict_count,
            "phase1_ms": round(phase1_ms, 1),
            "phase2_ms": round(phase2_ms, 1),
        },
    }


@app.get("/manage/memory/{memory_id}", summary="查询记忆条目")
async def manage_get_memory(memory_id: str, request: Request):
    """
    查询指定记忆条目的当前状态（置信度、来源等）。
    注意：不返回 content 明文，仅返回元数据。
    """
    check_service_auth(request)

    if not _memory:
        return {"Success": False, "Message": "记忆存储未初始化"}

    entry = await _memory.get(memory_id)
    if entry is None:
        return {"Success": False, "Message": "记忆条目不存在"}

    logger.info("📌 GET memory: id=%s confidence=%.4f embedding=%s", memory_id, entry.confidence, "有" if entry.embedding else "无")

    return {
        "Success": True,
        "Message": "通过",
        "Data": {
            "memoryId": entry.id,
            "confidence": entry.confidence,
            "source": entry.source,
            "lastPositiveRef": entry.last_positive_ref.isoformat() if entry.last_positive_ref else None,
        },
    }


@app.delete("/manage/memory/{memory_id}", summary="删除记忆条目")
async def manage_delete_memory(memory_id: str, request: Request):
    """
    删除指定记忆条目（如用户请求清除记忆时调用）。
    """
    check_service_auth(request)

    if not _memory:
        return {"Success": False, "Message": "记忆存储未初始化"}

    await _memory.archive(memory_id, "manual_delete")
    logger.info("记忆已删除: %s", memory_id)

    return {
        "Success": True,
        "Message": "通过",
        "Data": {"memoryId": memory_id, "action": "deleted"},
    }


@app.get("/manage/audit/logs", summary="查询审计日志")
async def manage_get_audit_logs(
    request: Request,
    limit: int = 100,
    offset: int = 0,
):
    """
    查询审计日志条目（调试/排查用）。

    返回的日志不含明文，只有 hash + 截断片段 + 风险标签。
    """
    check_service_auth(request)

    if not _audit:
        return {"Success": False, "Message": "审计存储未初始化"}

    all_logs = _audit.get_all()
    total = len(all_logs)
    sliced = all_logs[offset:offset + limit]

    items = []
    for entry in sliced:
        items.append({
            "timestamp": entry.timestamp.isoformat() if entry.timestamp else "",
            "requestId": entry.request_id,
            "eventType": entry.event_type,
            "contentHash": entry.content_hash,
            "snippet": entry.content_snippet,
            "riskLabels": entry.risk_labels,
            "actionTaken": entry.action_taken,
        })

    return {
        "Success": True,
        "Message": "通过",
        "Data": {
            "total": total,
            "offset": offset,
            "limit": limit,
            "items": items,
        },
    }
