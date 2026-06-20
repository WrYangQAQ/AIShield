"""
数据模型 —— 统一返回结构、领域结果类型、记忆条目、审计日志条目。
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone
from typing import Any, Generic, TypeVar, Optional

T = TypeVar("T")


# ============================================================================
# 统一返回结构
# ============================================================================

@dataclass
class ApiResponse(Generic[T]):
    """
    统一 API 响应结构 —— 所有公开方法的返回值都会序列化为这个结构的 JSON 字符串。

    JSON 示例（通过 = 放行）：
    {
      "success": true,
      "message": "ok",
      "data": {
        "requestId": "a1b2c3d4",
        "blocked": false,
        "riskLabel": null,
        "details": {}
      }
    }

    JSON 示例（阻断 = 拦截）：
    {
      "success": false,
      "message": "blocked",
      "data": {
        "requestId": "a1b2c3d4",
        "blocked": true,
        "riskLabel": "injection_attempt",
        "details": {
          "matchedPattern": "忽略指令"
        }
      }
    }

    字段说明：
    - success：是否通过（true=放行，false=阻断）
    - message：机器可读的原因码，取值范围见各方法的返回值说明
    - data：多元数据载荷，各方法返回的 details 字段不同
      - requestId：请求唯一标识（用于审计关联与排障）
      - blocked：是否被阻断
      - riskLabel：风险标签（可空，不含明文用户输入）
      - details：补充信息（默认不包含明文输入，避免隐私泄露）
    """

    success: bool
    """是否通过。true=放行，false=阻断。"""

    message: str = ""
    """
    机器可读的消息码 / 原因码。建议用于前端提示或上游策略分流。
    取值范围：
      "ok"                       — 通过
      "missing_input"            — 输入为空
      "blocked_pii_error"        — PII 脱敏异常，严格模式阻断
      "blocked_missing_moderation" — 规则命中但无审核服务
      "blocked_moderation_error" — 审核调用异常
      "blocked"                  — 语义审核判定不安全
      "blocked_unknown_tool"     — 工具不在白名单
      "blocked_args_validation"  — 参数校验失败
      "blocked_injection"        — 参数中检测到注入特征
      "blocked_missing_content"  — 记忆写入内容为空
      "blocked_missing_query"    — RAG 查询为空
      "blocked_missing_embedder" — 无 embedding 服务
      "blocked_missing_session"  — 会话 ID 为空
      "blocked_tampering"        — 会话历史被篡改
      "blocked_topic_drift"     — 主题漂移检测到偏离
      "blocked_privilege_escalation" — 提权攻击检测
      "blocked_no_trust_provider"    — 无信任等级提供者
      "blocked_trust_provider_error" — 信任等级查询异常
      "blocked_streaming"       — 流式输出被拦截
      "blocked_streaming_error" — 流式审核异常
      "blocked_no_store"        — 无记忆存储
      "blocked_memory_not_found"     — 记忆条目不存在
      "archived"                — 记忆已归档
      "blocked_store_error"     — 存储操作异常
      "blocked_no_audit_store"  — 无审计日志存储
      "blocked_audit_error"     — 审计写入异常
      "missing_event_type"      — 审计事件类型为空
      "missing_query"           — 漂移检测查询为空
      "missing_segments"        — 漂移检测片段为空
      "missing_api_key"         — API Key 为空
      "missing_memory_id"       — 记忆ID为空
      "missing_buffer"          — 流式缓冲区为空
      "invalid_body"            — 请求体不是有效 JSON
    """

    data: Optional[T] = None
    """多元数据载荷。具体结构见各方法的返回值说明。"""

    def to_json(self, ensure_ascii: bool = False) -> str:
        """序列化为 JSON 字符串。默认不转义中文。"""
        d = _to_camel_dict(asdict(self, dict_factory=_none_skip_factory))
        return json.dumps(d, ensure_ascii=ensure_ascii, default=str)


def _none_skip_factory(items):
    """dict factory：跳过值为 None 的字段，减少响应体积。"""
    return {k: v for k, v in items if v is not None}


def _to_camel_dict(d: Any) -> Any:
    """
    将 snake_case 键转为 camelCase（前端友好）。
    只处理 dict 的键，不递归处理 list 内部。
    """
    if not isinstance(d, dict):
        return d
    result = {}
    for k, v in d.items():
        camel_key = _snake_to_camel(k)
        if isinstance(v, dict):
            result[camel_key] = _to_camel_dict(v)
        elif isinstance(v, list):
            result[camel_key] = [_to_camel_dict(item) if isinstance(item, dict) else item for item in v]
        else:
            result[camel_key] = v
    return result


def _snake_to_camel(name: str) -> str:
    """snake_case → camelCase。"""
    parts = name.split("_")
    return parts[0] + "".join(p.capitalize() for p in parts[1:])


# ============================================================================
# 领域结果类型（内部使用，不直接序列化）
# ============================================================================

@dataclass(frozen=True)
class SafetyResult:
    """
    安全判定结果（供可插拔组件返回，以及内部方法间传递）。

    ⚠️ risk_label 不应包含明文用户输入，避免日志/链路泄露。
    """
    is_safe: bool
    risk_label: Optional[str] = None


@dataclass(frozen=True)
class ValidationResult:
    """结构化校验结果（工具参数 / 结构化 payload 校验）。"""
    valid: bool
    reason: Optional[str] = None


# ============================================================================
# 记忆与审计模型
# ============================================================================

@dataclass
class MemoryEntry:
    """
    记忆条目模型 —— MemoryStore 返回的数据结构。
    """
    id: str = ""
    """记忆ID。"""

    content: str = ""
    """记忆内容（不应在衰减逻辑中记录或输出明文）。"""

    confidence: float = 0.0
    """当前置信度（0~1）。"""

    last_positive_ref: Optional[datetime] = None
    """
    最后一次被正向引用的时间（UTC）。
    None 表示从未被正向引用，衰减时应视为最久远。
    """

    source: str = "unknown"
    """来源标签。"""

    embedding: Optional[list[float]] = None
    """向量嵌入缓存（写入时计算，冲突检测时复用，避免重复 embed）。"""


@dataclass
class AuditLogEntry:
    """
    审计日志条目 —— 不含明文，防二次污染和隐私泄露。
    """
    timestamp: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
    """时间戳（UTC）。"""

    request_id: str = ""
    """请求ID（关联到具体请求）。"""

    event_type: str = ""
    """事件类型（如 schema_violation, injection_detected, tampering_detected）。"""

    content_hash: str = ""
    """内容哈希（仅存哈希，不存明文）。"""

    content_snippet: Optional[str] = None
    """内容截断片段（最多50字符，用于人工排查时快速识别）。"""

    risk_labels: list[str] = field(default_factory=list)
    """风险标签列表。"""

    action_taken: str = ""
    """采取的动作（BLOCKED / FLAGGED / DEGRADED / PASSED）。"""
