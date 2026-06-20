"""
8 个默认接口实现的单元测试。

覆盖：
  - OpenAIModerationClient（无 Key 放行、有 Key mock 验证）
  - RegexNerRedactor（手机号/身份证号脱敏）
  - OpenAIEmbedder（无 Key 抛异常、有 Key mock 验证）
  - InMemoryHistoryProvider（set_history / get_history）
  - JiebaEntityExtractor（实体/关键词提取、jieba 不可用时降级）
  - InMemoryTrustLevelProvider（set_level / get_trust_level / 非法等级）
  - InMemoryMemoryStore（put / get / update / archive）
  - InMemoryAuditLogStore（insert / get_all / count / 文件落盘）
"""

from __future__ import annotations

import importlib
import json
import os
import tempfile
from datetime import datetime, timezone
from unittest.mock import AsyncMock, patch, MagicMock

import pytest

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
from guard_algorithms.models import MemoryEntry, AuditLogEntry

# aiohttp 是否已安装（mock 测试需要）
_aiohttp_available = importlib.util.find_spec("aiohttp") is not None


# ============================================================================
# OpenAIModerationClient
# ============================================================================

class TestOpenAIModerationClient:
    """OpenAI Moderation API 客户端测试"""

    @pytest.mark.asyncio
    async def test_no_api_key_returns_unsafe(self):
        """无 API Key → Fail-Closed，视为不安全"""
        client = OpenAIModerationClient(api_key="")
        result = await client.check("你好世界")
        assert result.is_safe is False
        assert result.risk_label == "no_api_key"

    @pytest.mark.asyncio
    async def test_no_env_key_returns_unsafe(self):
        """无环境变量 → Fail-Closed，视为不安全"""
        with patch.dict(os.environ, {}, clear=True):
            client = OpenAIModerationClient()
            result = await client.check("test")
            assert result.is_safe is False

    @pytest.mark.asyncio
    @pytest.mark.skipif(not _aiohttp_available, reason="aiohttp 未安装")
    async def test_moderation_flagged(self):
        """审核 API 返回 flagged → 不安全"""
        client = OpenAIModerationClient(api_key="sk-test-key")

        mock_resp = MagicMock()
        mock_resp.status = 200
        mock_resp.json = AsyncMock(return_value={
            "results": [{"flagged": True, "categories": {"violence": True}}]
        })

        mock_session = AsyncMock()
        mock_session.post = MagicMock(return_value=mock_resp)
        mock_session.post.return_value.__aenter__ = AsyncMock(return_value=mock_resp)
        mock_session.post.return_value.__aexit__ = AsyncMock(return_value=False)
        mock_session.__aenter__ = AsyncMock(return_value=mock_session)
        mock_session.__aexit__ = AsyncMock(return_value=False)

        with patch("aiohttp.ClientSession", return_value=mock_session):
            result = await client.check("暴力内容")
            assert result.is_safe is False
            assert "violence" in (result.risk_label or "")

    @pytest.mark.asyncio
    @pytest.mark.skipif(not _aiohttp_available, reason="aiohttp 未安装")
    async def test_moderation_safe(self):
        """审核 API 返回未标记 → 安全"""
        client = OpenAIModerationClient(api_key="sk-test-key")

        mock_resp = MagicMock()
        mock_resp.status = 200
        mock_resp.json = AsyncMock(return_value={
            "results": [{"flagged": False, "categories": {}}]
        })

        mock_session = AsyncMock()
        mock_session.post = MagicMock(return_value=mock_resp)
        mock_session.post.return_value.__aenter__ = AsyncMock(return_value=mock_resp)
        mock_session.post.return_value.__aexit__ = AsyncMock(return_value=False)
        mock_session.__aenter__ = AsyncMock(return_value=mock_session)
        mock_session.__aexit__ = AsyncMock(return_value=False)

        with patch("aiohttp.ClientSession", return_value=mock_session):
            result = await client.check("正常内容")
            assert result.is_safe is True

    @pytest.mark.asyncio
    @pytest.mark.skipif(not _aiohttp_available, reason="aiohttp 未安装")
    async def test_moderation_http_error_returns_safe(self):
        """审核 API 返回非 200 → Fail-Open"""
        client = OpenAIModerationClient(api_key="sk-test-key")

        mock_resp = MagicMock()
        mock_resp.status = 500
        mock_resp.json = AsyncMock(return_value={})

        mock_session = AsyncMock()
        mock_session.post = MagicMock(return_value=mock_resp)
        mock_session.post.return_value.__aenter__ = AsyncMock(return_value=mock_resp)
        mock_session.post.return_value.__aexit__ = AsyncMock(return_value=False)
        mock_session.__aenter__ = AsyncMock(return_value=mock_session)
        mock_session.__aexit__ = AsyncMock(return_value=False)

        with patch("aiohttp.ClientSession", return_value=mock_session):
            result = await client.check("test")
            assert result.is_safe is True
            assert result.risk_label == "http_500"

    @pytest.mark.asyncio
    @pytest.mark.skipif(not _aiohttp_available, reason="aiohttp 未安装")
    async def test_moderation_exception_returns_safe(self):
        """审核 API 抛异常 → Fail-Open"""
        client = OpenAIModerationClient(api_key="sk-test-key")

        mock_session = MagicMock()
        mock_session.__aenter__ = AsyncMock(side_effect=Exception("connection error"))
        mock_session.__aexit__ = AsyncMock(return_value=False)

        with patch("aiohttp.ClientSession", return_value=mock_session):
            result = await client.check("test")
            assert result.is_safe is True
            assert result.risk_label == "moderation_error"

    def test_custom_base_url(self):
        """自定义 base_url"""
        client = OpenAIModerationClient(api_key="sk-test", base_url="https://custom.api.com/v1")
        assert client._base_url == "https://custom.api.com/v1"


# ============================================================================
# RegexNerRedactor
# ============================================================================

class TestRegexNerRedactor:
    """正则 PII 脱敏测试"""

    @pytest.mark.asyncio
    async def test_redact_phone_number(self):
        """手机号脱敏"""
        redactor = RegexNerRedactor()
        text = "请拨打13812345678联系我"
        sanitized, labels = await redactor.redact(text)
        assert "13812345678" not in sanitized
        assert "[PHONE_MASKED]" in sanitized

    @pytest.mark.asyncio
    async def test_redact_id_card(self):
        """身份证号脱敏"""
        redactor = RegexNerRedactor()
        text = "身份证号110101199001011234"
        sanitized, labels = await redactor.redact(text)
        assert "110101199001011234" not in sanitized

    @pytest.mark.asyncio
    async def test_redact_clean_text(self):
        """无 PII 的文本不变"""
        redactor = RegexNerRedactor()
        text = "今天天气不错"
        sanitized, labels = await redactor.redact(text)
        assert sanitized == text
        assert len(labels) == 0

    @pytest.mark.asyncio
    async def test_redact_api_key(self):
        """API Key 脱敏"""
        redactor = RegexNerRedactor()
        text = "密钥是sk-abcdefghijklmnopqrst"
        sanitized, labels = await redactor.redact(text)
        assert "sk-abcdefghijklmnopqrst" not in sanitized


# ============================================================================
# OpenAIEmbedder
# ============================================================================

class TestOpenAIEmbedder:
    """OpenAI Embeddings API 客户端测试"""

    @pytest.mark.asyncio
    async def test_no_api_key_raises(self):
        """无 API Key → 抛 RuntimeError（不依赖 aiohttp）"""
        embedder = OpenAIEmbedder(api_key="")
        with pytest.raises(RuntimeError, match="OPENAI_API_KEY"):
            await embedder.embed("test")

    @pytest.mark.asyncio
    @pytest.mark.skipif(not _aiohttp_available, reason="aiohttp 未安装")
    async def test_embed_success(self):
        """正常嵌入返回"""
        embedder = OpenAIEmbedder(api_key="sk-test-key")

        mock_resp = MagicMock()
        mock_resp.status = 200
        mock_resp.json = AsyncMock(return_value={
            "data": [{"embedding": [0.1, 0.2, 0.3]}]
        })

        mock_session = AsyncMock()
        mock_session.post = MagicMock(return_value=mock_resp)
        mock_session.post.return_value.__aenter__ = AsyncMock(return_value=mock_resp)
        mock_session.post.return_value.__aexit__ = AsyncMock(return_value=False)
        mock_session.__aenter__ = AsyncMock(return_value=mock_session)
        mock_session.__aexit__ = AsyncMock(return_value=False)

        with patch("aiohttp.ClientSession", return_value=mock_session):
            result = await embedder.embed("hello world")
            assert result == [0.1, 0.2, 0.3]

    @pytest.mark.asyncio
    @pytest.mark.skipif(not _aiohttp_available, reason="aiohttp 未安装")
    async def test_embed_http_error(self):
        """API 返回非 200 → 抛 RuntimeError"""
        embedder = OpenAIEmbedder(api_key="sk-test-key")

        mock_resp = MagicMock()
        mock_resp.status = 401
        mock_resp.json = AsyncMock(return_value={})
        mock_resp.text = AsyncMock(return_value="Unauthorized")

        mock_session = AsyncMock()
        mock_session.post = MagicMock(return_value=mock_resp)
        mock_session.post.return_value.__aenter__ = AsyncMock(return_value=mock_resp)
        mock_session.post.return_value.__aexit__ = AsyncMock(return_value=False)
        mock_session.__aenter__ = AsyncMock(return_value=mock_session)
        mock_session.__aexit__ = AsyncMock(return_value=False)

        with patch("aiohttp.ClientSession", return_value=mock_session):
            with pytest.raises(RuntimeError, match="401"):
                await embedder.embed("test")

    def test_custom_model(self):
        """自定义 model"""
        embedder = OpenAIEmbedder(api_key="sk-test", model="text-embedding-3-large")
        assert embedder._model == "text-embedding-3-large"


# ============================================================================
# InMemoryHistoryProvider
# ============================================================================

class TestInMemoryHistoryProvider:
    """内存历史提供者测试"""

    @pytest.mark.asyncio
    async def test_get_empty_history(self):
        """空 session → 返回空列表"""
        provider = InMemoryHistoryProvider()
        result = await provider.get_history("nonexistent")
        assert result == []

    @pytest.mark.asyncio
    async def test_set_and_get_history(self):
        """设置后可获取"""
        provider = InMemoryHistoryProvider()
        history = [("msg-1", "hash1"), ("msg-2", "hash2")]
        provider.set_history("session-1", history)
        result = await provider.get_history("session-1")
        assert result == history

    @pytest.mark.asyncio
    async def test_overwrite_history(self):
        """重复 set 覆盖"""
        provider = InMemoryHistoryProvider()
        provider.set_history("s1", [("a", "h1")])
        provider.set_history("s1", [("b", "h2")])
        result = await provider.get_history("s1")
        assert result == [("b", "h2")]

    @pytest.mark.asyncio
    async def test_multiple_sessions(self):
        """多个 session 互不干扰"""
        provider = InMemoryHistoryProvider()
        provider.set_history("s1", [("a", "h1")])
        provider.set_history("s2", [("b", "h2")])
        assert await provider.get_history("s1") == [("a", "h1")]
        assert await provider.get_history("s2") == [("b", "h2")]


# ============================================================================
# JiebaEntityExtractor
# ============================================================================

class TestJiebaEntityExtractor:
    """jieba 实体提取测试"""

    @pytest.mark.asyncio
    async def test_extract_entities(self):
        """提取实体返回非空列表"""
        extractor = JiebaEntityExtractor()
        entities = await extractor.extract_entities("如何制作蛋糕和面包")
        assert isinstance(entities, list)
        assert len(entities) > 0

    @pytest.mark.asyncio
    async def test_extract_keywords(self):
        """提取关键词返回非空列表"""
        extractor = JiebaEntityExtractor()
        keywords = await extractor.extract_keywords("如何制作蛋糕和面包")
        assert isinstance(keywords, list)
        assert len(keywords) > 0

    @pytest.mark.asyncio
    async def test_entities_are_lowercase(self):
        """实体应该小写化"""
        extractor = JiebaEntityExtractor()
        entities = await extractor.extract_entities("Python Programming")
        for e in entities:
            assert e == e.lower()

    @pytest.mark.asyncio
    async def test_entities_are_deduplicated(self):
        """实体应该去重"""
        extractor = JiebaEntityExtractor()
        entities = await extractor.extract_entities("蛋糕蛋糕蛋糕")
        assert len(entities) == len(set(entities))

    @pytest.mark.asyncio
    async def test_empty_input(self):
        """空输入 → 空列表"""
        extractor = JiebaEntityExtractor()
        entities = await extractor.extract_entities("")
        assert entities == []

    @pytest.mark.asyncio
    async def test_fallback_without_jieba(self):
        """jieba 不可用时降级为简单分词"""
        extractor = JiebaEntityExtractor()
        extractor._jieba_available = False
        entities = await extractor.extract_entities("如何制作蛋糕")
        assert isinstance(entities, list)
        assert any("蛋糕" in e for e in entities)

    @pytest.mark.asyncio
    async def test_keywords_fallback_without_jieba(self):
        """jieba 不可用时关键词降级"""
        extractor = JiebaEntityExtractor()
        extractor._jieba_available = False
        keywords = await extractor.extract_keywords("制作蛋糕")
        assert isinstance(keywords, list)

    @pytest.mark.asyncio
    async def test_stop_words_filtered(self):
        """停用词应被过滤"""
        extractor = JiebaEntityExtractor()
        entities = await extractor.extract_entities("我的蛋糕")
        for e in entities:
            assert e not in {"的", "我"}


# ============================================================================
# InMemoryTrustLevelProvider
# ============================================================================

class TestInMemoryTrustLevelProvider:
    """内存信任等级提供者测试"""

    @pytest.mark.asyncio
    async def test_default_is_untrusted(self):
        """未设置的 Key → untrusted"""
        provider = InMemoryTrustLevelProvider()
        result = await provider.get_trust_level("unknown-key")
        assert result == "untrusted"

    @pytest.mark.asyncio
    async def test_set_and_get_level(self):
        """设置后可获取"""
        provider = InMemoryTrustLevelProvider()
        provider.set_level("sk-admin", "admin")
        result = await provider.get_trust_level("sk-admin")
        assert result == "admin"

    @pytest.mark.asyncio
    async def test_all_valid_levels(self):
        """所有合法等级都能设置"""
        provider = InMemoryTrustLevelProvider()
        for level in ["system", "admin", "user", "untrusted"]:
            provider.set_level(f"key-{level}", level)
            assert await provider.get_trust_level(f"key-{level}") == level

    def test_invalid_level_raises(self):
        """无效等级 → ValueError"""
        provider = InMemoryTrustLevelProvider()
        with pytest.raises(ValueError, match="无效信任等级"):
            provider.set_level("key", "superadmin")

    @pytest.mark.asyncio
    async def test_overwrite_level(self):
        """重复 set 覆盖"""
        provider = InMemoryTrustLevelProvider()
        provider.set_level("k1", "user")
        provider.set_level("k1", "admin")
        assert await provider.get_trust_level("k1") == "admin"


# ============================================================================
# InMemoryMemoryStore
# ============================================================================

class TestInMemoryMemoryStore:
    """内存记忆存储测试"""

    @pytest.mark.asyncio
    async def test_get_nonexistent(self):
        """不存在的记忆 → None"""
        store = InMemoryMemoryStore()
        result = await store.get("nonexistent")
        assert result is None

    @pytest.mark.asyncio
    async def test_put_and_get(self):
        """存入后可获取"""
        store = InMemoryMemoryStore()
        entry = MemoryEntry(
            id="mem-1",
            content="测试内容",
            confidence=0.8,
            last_positive_ref=datetime.now(timezone.utc),
            source="user",
        )
        store.put("mem-1", entry)
        result = await store.get("mem-1")
        assert result is not None
        assert result.id == "mem-1"
        assert result.content == "测试内容"
        assert result.confidence == 0.8

    @pytest.mark.asyncio
    async def test_update_confidence(self):
        """更新置信度"""
        store = InMemoryMemoryStore()
        entry = MemoryEntry(id="mem-1", content="test", confidence=0.9, source="user")
        store.put("mem-1", entry)
        await store.update("mem-1", 0.5)
        result = await store.get("mem-1")
        assert result.confidence == 0.5
        assert result.content == "test"

    @pytest.mark.asyncio
    async def test_update_nonexistent_is_noop(self):
        """更新不存在的记忆 → 无操作（不报错）"""
        store = InMemoryMemoryStore()
        await store.update("nonexistent", 0.5)

    @pytest.mark.asyncio
    async def test_archive_removes_entry(self):
        """归档后记忆消失"""
        store = InMemoryMemoryStore()
        entry = MemoryEntry(id="mem-1", content="test", confidence=0.5, source="user")
        store.put("mem-1", entry)
        await store.archive("mem-1", "low_confidence")
        result = await store.get("mem-1")
        assert result is None

    @pytest.mark.asyncio
    async def test_archive_nonexistent_is_noop(self):
        """归档不存在的记忆 → 无操作"""
        store = InMemoryMemoryStore()
        await store.archive("nonexistent", "test")

    @pytest.mark.asyncio
    async def test_put_preserves_last_positive_ref(self):
        """last_positive_ref 正确保存"""
        store = InMemoryMemoryStore()
        ref_time = datetime(2026, 1, 1, 12, 0, 0, tzinfo=timezone.utc)
        entry = MemoryEntry(id="m1", content="test", confidence=0.8, last_positive_ref=ref_time, source="user")
        store.put("m1", entry)
        result = await store.get("m1")
        assert result.last_positive_ref == ref_time

    @pytest.mark.asyncio
    async def test_update_preserves_last_positive_ref(self):
        """更新置信度时 last_positive_ref 不变"""
        store = InMemoryMemoryStore()
        ref_time = datetime(2026, 1, 1, 12, 0, 0, tzinfo=timezone.utc)
        entry = MemoryEntry(id="m1", content="test", confidence=0.8, last_positive_ref=ref_time, source="user")
        store.put("m1", entry)
        await store.update("m1", 0.6)
        result = await store.get("m1")
        assert result.last_positive_ref == ref_time
        assert result.confidence == 0.6


# ============================================================================
# InMemoryAuditLogStore
# ============================================================================

class TestInMemoryAuditLogStore:
    """内存审计日志存储测试"""

    @pytest.mark.asyncio
    async def test_insert_and_count(self):
        """插入后计数正确"""
        store = InMemoryAuditLogStore()
        entry = AuditLogEntry(
            event_type="injection_detected",
            content_hash="abc123",
            risk_labels=["injection"],
            action_taken="BLOCKED",
            request_id="req-1",
        )
        await store.insert(entry)
        assert store.count() == 1

    @pytest.mark.asyncio
    async def test_get_all(self):
        """获取全部日志"""
        store = InMemoryAuditLogStore()
        for i in range(3):
            await store.insert(AuditLogEntry(
                event_type=f"event_{i}",
                content_hash=f"hash_{i}",
                request_id=f"req-{i}",
            ))
        logs = store.get_all()
        assert len(logs) == 3

    @pytest.mark.asyncio
    async def test_empty_store(self):
        """空存储"""
        store = InMemoryAuditLogStore()
        assert store.count() == 0
        assert store.get_all() == []

    @pytest.mark.asyncio
    async def test_file_persistence(self):
        """JSONL 文件落盘"""
        with tempfile.NamedTemporaryFile(mode="w", suffix=".jsonl", delete=False) as f:
            file_path = f.name

        try:
            store = InMemoryAuditLogStore(file_path=file_path)
            await store.insert(AuditLogEntry(
                event_type="test_event",
                content_hash="h1",
                content_snippet="测试片段",
                risk_labels=["label1"],
                action_taken="BLOCKED",
                request_id="req-1",
            ))

            with open(file_path, "r", encoding="utf-8") as f:
                lines = f.readlines()
                assert len(lines) == 1
                data = json.loads(lines[0])
                assert data["eventType"] == "test_event"
                assert data["contentHash"] == "h1"
                assert data["snippet"] == "测试片段"
                assert data["riskLabels"] == ["label1"]
                assert data["actionTaken"] == "BLOCKED"
                assert data["requestId"] == "req-1"
        finally:
            os.unlink(file_path)

    @pytest.mark.asyncio
    async def test_file_persistence_multiple(self):
        """多条日志追加写入文件"""
        with tempfile.NamedTemporaryFile(mode="w", suffix=".jsonl", delete=False) as f:
            file_path = f.name

        try:
            store = InMemoryAuditLogStore(file_path=file_path)
            for i in range(3):
                await store.insert(AuditLogEntry(
                    event_type=f"event_{i}",
                    content_hash=f"hash_{i}",
                    request_id=f"req-{i}",
                ))

            with open(file_path, "r", encoding="utf-8") as f:
                lines = f.readlines()
                assert len(lines) == 3
        finally:
            os.unlink(file_path)

    @pytest.mark.asyncio
    async def test_no_file_path_is_ok(self):
        """不设文件路径也能正常工作"""
        store = InMemoryAuditLogStore()
        await store.insert(AuditLogEntry(
            event_type="test",
            content_hash="h1",
            request_id="r1",
        ))
        assert store.count() == 1

    @pytest.mark.asyncio
    async def test_preserves_entry_fields(self):
        """日志条目所有字段都保存"""
        store = InMemoryAuditLogStore()
        ts = datetime(2026, 6, 11, 10, 0, 0, tzinfo=timezone.utc)
        entry = AuditLogEntry(
            timestamp=ts,
            request_id="r1",
            event_type="injection",
            content_hash="h1",
            content_snippet="前50字",
            risk_labels=["injection", "jailbreak"],
            action_taken="BLOCKED",
        )
        await store.insert(entry)
        logs = store.get_all()
        assert len(logs) == 1
        assert logs[0].event_type == "injection"
        assert logs[0].content_hash == "h1"
        assert logs[0].content_snippet == "前50字"
        assert logs[0].risk_labels == ["injection", "jailbreak"]
        assert logs[0].action_taken == "BLOCKED"
