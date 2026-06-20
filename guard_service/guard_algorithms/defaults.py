"""
8 个可插拔接口的默认实现 —— 开箱即用。

- ModerationClient → 调用 OpenAI Moderation API（需配置 OPENAI_API_KEY）
- NerRedactor → 正则兜底（复用 pii 模块），无 NER 模型依赖
- Embedder → 调用 OpenAI Embeddings API（需配置 OPENAI_API_KEY）
- ServerHistoryProvider → 内存 dict 存储（适合单实例开发，生产换 Redis/DB）
- EntityExtractor → jieba 分词 + 停用词过滤
- TrustLevelProvider → 内存 dict 存储（适合开发，生产换鉴权服务）
- MemoryStore → 内存 dict 存储（适合开发，生产换 DB）
- AuditLogStore → 内存列表 + 可选文件落盘
"""

from __future__ import annotations

import json
import os
from datetime import datetime, timezone
from typing import Optional

from .interfaces import (
    ModerationClient,
    NerRedactor,
    Embedder,
    ServerHistoryProvider,
    EntityExtractor,
    TrustLevelProvider,
    MemoryStore,
    AuditLogStore,
)
from .models import SafetyResult, MemoryEntry, AuditLogEntry


# ============================================================================
# ModerationClient —— OpenAI Moderation API
# ============================================================================

class OpenAIModerationClient(ModerationClient):
    """
    调用 OpenAI Moderation API 进行语义审核。

    需要环境变量：OPENAI_API_KEY（或构造时传入）
    可选环境变量：OPENAI_BASE_URL（自定义端点，如 DeepSeek 兼容接口）
    """

    def __init__(self, api_key: Optional[str] = None, base_url: Optional[str] = None):
        self._api_key = api_key or os.environ.get("OPENAI_API_KEY", "")
        self._base_url = base_url or os.environ.get("OPENAI_BASE_URL", "https://api.openai.com/v1")

    async def check(self, text: str) -> SafetyResult:
        if not self._api_key:
            # 无 API Key 时视为不安全（Fail-Closed：宁可误拦，不可漏放）
            # 规则命中 + 无审核能力 → 应阻断而非放行
            return SafetyResult(is_safe=False, risk_label="no_api_key")

        import aiohttp

        url = f"{self._base_url}/moderations"
        headers = {
            "Authorization": f"Bearer {self._api_key}",
            "Content-Type": "application/json",
        }
        payload = {"input": text}

        try:
            async with aiohttp.ClientSession() as session:
                async with session.post(url, headers=headers, json=payload, timeout=aiohttp.ClientTimeout(total=5)) as resp:
                    if resp.status != 200:
                        return SafetyResult(is_safe=True, risk_label=f"http_{resp.status}")
                    data = await resp.json()
                    result = data.get("results", [{}])[0]
                    flagged = result.get("flagged", False)
                    categories = [k for k, v in result.get("categories", {}).items() if v]
                    return SafetyResult(is_safe=not flagged, risk_label=",".join(categories) if categories else None)
        except Exception:
            # 审核服务异常时 Fail-Open（不误杀），严格模式由 core 层兜底
            return SafetyResult(is_safe=True, risk_label="moderation_error")


# ============================================================================
# NerRedactor —— 正则兜底
# ============================================================================

class RegexNerRedactor(NerRedactor):
    """
    正则兜底的 PII 脱敏。
    只做正则匹配（身份证号、手机号、API Key 等），无 NER 模型依赖。
    作为 NerRedactor 接口的轻量实现 —— 走一遍正则匹配就返回。
    """

    async def redact(self, text: str) -> tuple[str, list[str]]:
        from .pii import PiiSanitizer
        from .config import GuardConfig
        config = GuardConfig()
        sanitized, labels, _error = await PiiSanitizer.sanitize(text, config, ner_redactor=None)
        return sanitized, labels


# ============================================================================
# Embedder —— OpenAI Embeddings API
# ============================================================================

class OpenAIEmbedder(Embedder):
    """
    调用 OpenAI 兼容 Embeddings API（支持硅基流动/智谱/Jina 等）。

    需要环境变量：OPENAI_API_KEY（或 EMBEDDING_API_KEY）
    可选环境变量：OPENAI_BASE_URL / EMBEDDING_BASE_URL / EMBEDDING_MODEL
    """

    def __init__(self, api_key: Optional[str] = None, base_url: Optional[str] = None, model: Optional[str] = None):
        self._api_key = (
            api_key
            or os.environ.get("EMBEDDING_API_KEY", "")
            or os.environ.get("OPENAI_API_KEY", "")
        )
        self._base_url = (
            base_url
            or os.environ.get("EMBEDDING_BASE_URL", "")
            or os.environ.get("OPENAI_BASE_URL", "https://api.openai.com/v1")
        )
        self._model = model or os.environ.get("EMBEDDING_MODEL", "text-embedding-3-small")
        self._session: Optional[object] = None

    def _get_session(self):
        """懒加载 aiohttp session（复用连接池）。"""
        if self._session is None or self._session.closed:
            import aiohttp
            self._session = aiohttp.ClientSession()
        return self._session

    async def embed(self, text: str) -> list[float]:
        if not self._api_key:
            raise RuntimeError("Embedding API Key 未配置（设置 EMBEDDING_API_KEY 或 OPENAI_API_KEY）")

        import aiohttp

        session = self._get_session()
        url = f"{self._base_url}/embeddings"
        headers = {
            "Authorization": f"Bearer {self._api_key}",
            "Content-Type": "application/json",
        }
        payload = {"input": [text], "model": self._model}

        async with session.post(url, headers=headers, json=payload, timeout=aiohttp.ClientTimeout(total=30)) as resp:
            if resp.status != 200:
                body = await resp.text()
                raise RuntimeError(f"Embedding API 返回 {resp.status}: {body[:200]}")
            data = await resp.json()
            return data["data"][0]["embedding"]

    async def embed_batch(self, texts: list[str]) -> list[list[float]]:
        """批量 embedding，一次 API 调用处理多条文本。"""
        if not self._api_key:
            raise RuntimeError("Embedding API Key 未配置")

        import aiohttp

        session = self._get_session()
        url = f"{self._base_url}/embeddings"
        headers = {
            "Authorization": f"Bearer {self._api_key}",
            "Content-Type": "application/json",
        }

        # 多数 API 限制单次 <= 64 条
        all_embeddings: list[list[float]] = []
        batch_size = 64
        for i in range(0, len(texts), batch_size):
            batch = texts[i:i + batch_size]
            payload = {"input": batch, "model": self._model}
            async with session.post(url, headers=headers, json=payload, timeout=aiohttp.ClientTimeout(total=60)) as resp:
                if resp.status != 200:
                    body = await resp.text()
                    raise RuntimeError(f"Embedding API 返回 {resp.status}: {body[:200]}")
                data = await resp.json()
                # 按 index 排序确保顺序正确
                sorted_data = sorted(data["data"], key=lambda d: d["index"])
                all_embeddings.extend(d["embedding"] for d in sorted_data)

        return all_embeddings

    async def close(self):
        """关闭 HTTP session。"""
        if self._session and not self._session.closed:
            await self._session.close()
            self._session = None

    def is_ready(self) -> bool:
        return bool(self._api_key)


# ============================================================================
# ServerHistoryProvider —— 内存存储
# ============================================================================

class InMemoryHistoryProvider(ServerHistoryProvider):
    """
    内存 dict 存储的服务端历史（开发用，生产换 Redis/DB）。

    用法：
        provider = InMemoryHistoryProvider()
        provider.set_history("session-1", [("msg-1", "hash1"), ("msg-2", "hash2")])
    """

    def __init__(self):
        self._store: dict[str, list[tuple[str, str]]] = {}

    def set_history(self, session_id: str, history: list[tuple[str, str]]) -> None:
        """手动设置某个 session 的历史（测试/开发用）"""
        self._store[session_id] = history

    async def get_history(self, session_id: str) -> list[tuple[str, str]]:
        return self._store.get(session_id, [])


# ============================================================================
# EntityExtractor —— jieba 分词
# ============================================================================

class JiebaEntityExtractor(EntityExtractor):
    """
    基于 jieba 的实体/关键词提取。

    需要：pip install jieba
    如果 jieba 未安装，自动降级为简单分词。
    """

    _STOP_WORDS = frozenset({
        "的", "了", "在", "是", "我", "有", "和", "就", "不", "人", "都", "一",
        "一个", "上", "也", "很", "到", "说", "要", "去", "你", "会", "着", "没有",
        "看", "好", "自己", "这", "他", "她", "它", "们", "那", "个", "吗", "吧",
        "啊", "呢", "嗯", "哦", "把", "被", "让", "给", "从", "对", "向", "为",
        "the", "a", "an", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could",
        "should", "may", "might", "shall", "can", "need", "dare", "ought",
        "i", "you", "he", "she", "it", "we", "they", "me", "him", "her",
        "us", "them", "my", "your", "his", "its", "our", "their",
        "this", "that", "these", "those", "and", "or", "but", "if",
        "in", "on", "at", "to", "for", "of", "with", "by", "from",
    })

    def __init__(self):
        self._jieba_available = False
        try:
            import jieba  # noqa: F401
            self._jieba_available = True
        except (ImportError, AttributeError, Exception):
            pass

    async def extract_entities(self, text: str) -> list[str]:
        if self._jieba_available:
            import jieba
            words = [w for w in jieba.cut(text) if len(w) > 1 and w not in self._STOP_WORDS]
        else:
            # 简单降级：按标点和空白切分
            import re
            words = [w for w in re.split(r'[，。！？\s,!?;；：:]+', text) if len(w) > 1 and w not in self._STOP_WORDS]
        return list(set(w.lower() for w in words))

    async def extract_keywords(self, text: str) -> list[str]:
        if self._jieba_available:
            import jieba.analyse
            keywords = jieba.analyse.extract_tags(text, topK=10)
        else:
            # 降级：和 extract_entities 一样
            import re
            keywords = [w for w in re.split(r'[，。！？\s,!?;；：:]+', text) if len(w) > 1 and w not in self._STOP_WORDS]
        return list(set(k.lower() for k in keywords))


# ============================================================================
# TrustLevelProvider —— 内存存储
# ============================================================================

class InMemoryTrustLevelProvider(TrustLevelProvider):
    """
    内存 dict 的信任等级提供者（开发用，生产换鉴权服务）。

    用法：
        provider = InMemoryTrustLevelProvider()
        provider.set_level("api-key-1", "admin")
    """

    _VALID_LEVELS = {"system", "admin", "user", "untrusted"}

    def __init__(self):
        self._store: dict[str, str] = {}

    def set_level(self, api_key: str, level: str) -> None:
        """手动设置 API Key 对应的信任等级（测试/开发用）"""
        if level not in self._VALID_LEVELS:
            raise ValueError(f"无效信任等级：{level}，可选：{self._VALID_LEVELS}")
        self._store[api_key] = level

    async def get_trust_level(self, api_key: str) -> str:
        return self._store.get(api_key, "untrusted")


# ============================================================================
# MemoryStore —— 内存存储
# ============================================================================

class InMemoryMemoryStore(MemoryStore):
    """
    内存 dict 的记忆存储（开发用，生产换 DB）。

    用法：
        store = InMemoryMemoryStore()
        store.put("mem-1", MemoryEntry(...))
    """

    def __init__(self):
        self._store: dict[str, MemoryEntry] = {}

    def put(self, memory_id: str, entry: MemoryEntry) -> None:
        """手动放入一条记忆（测试/开发用）"""
        self._store[memory_id] = entry

    async def get(self, memory_id: str) -> Optional[MemoryEntry]:
        return self._store.get(memory_id)

    async def update(self, memory_id: str, confidence: float,
                     last_positive_ref=None) -> None:
        if memory_id in self._store:
            entry = self._store[memory_id]
            self._store[memory_id] = MemoryEntry(
                id=entry.id,
                content=entry.content,
                confidence=confidence,
                last_positive_ref=last_positive_ref if last_positive_ref is not None else entry.last_positive_ref,
                source=entry.source,
                embedding=entry.embedding,
            )

    async def archive(self, memory_id: str, reason: str) -> None:
        self._store.pop(memory_id, None)

    def list_by_source(self, source: str) -> list[MemoryEntry]:
        """列出指定来源的所有记忆条目。"""
        return [entry for entry in self._store.values() if entry.source == source]


# ============================================================================
# AuditLogStore —— 内存列表 + 可选文件落盘
# ============================================================================

class InMemoryAuditLogStore(AuditLogStore):
    """
    内存列表的审计日志存储 + 可选 JSON 文件落盘。

    用法：
        store = InMemoryAuditLogStore()                    # 只存内存
        store = InMemoryAuditLogStore("/var/log/audit.jsonl")  # 同时写文件
    """

    def __init__(self, file_path: Optional[str] = None):
        self._logs: list[AuditLogEntry] = []
        self._file_path = file_path

    async def insert(self, entry: AuditLogEntry) -> None:
        self._write_entry(entry)

    def append(self, raw: dict) -> None:
        """同步写入审计日志（从 dict 构建 AuditLogEntry）。

        供 guard_service.py 的 _auto_audit 调用，无需 await。
        """
        entry = AuditLogEntry(
            timestamp=datetime.fromisoformat(raw["timestamp"]) if "timestamp" in raw else datetime.now(timezone.utc),
            request_id=raw.get("requestId", ""),
            event_type=raw.get("eventType", ""),
            content_hash=raw.get("contentHash", ""),
            content_snippet=raw.get("snippet"),
            risk_labels=raw.get("riskLabels", []),
            action_taken=raw.get("actionTaken", ""),
        )
        self._write_entry(entry)

    def _write_entry(self, entry: AuditLogEntry) -> None:
        """实际写入逻辑：内存 + 可选文件落盘。"""
        self._logs.append(entry)
        if self._file_path:
            line = json.dumps({
                "eventType": entry.event_type,
                "contentHash": entry.content_hash,
                "snippet": entry.content_snippet,
                "riskLabels": entry.risk_labels,
                "actionTaken": entry.action_taken,
                "requestId": entry.request_id,
                "timestamp": entry.timestamp.isoformat() if isinstance(entry.timestamp, datetime) else str(entry.timestamp),
            }, ensure_ascii=False) + "\n"
            with open(self._file_path, "a", encoding="utf-8") as f:
                f.write(line)

    def get_all(self) -> list[AuditLogEntry]:
        """获取所有审计日志（测试/调试用）"""
        return list(self._logs)

    def count(self) -> int:
        return len(self._logs)
