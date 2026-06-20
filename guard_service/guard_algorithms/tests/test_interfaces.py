"""
interfaces.py 自动化测试 —— 8 个 ABC 接口的可实例化/抽象校验
"""

import pytest
from guard_algorithms.interfaces import (
    ModerationClient,
    NerRedactor,
    Embedder,
    ServerHistoryProvider,
    EntityExtractor,
    TrustLevelProvider,
    MemoryStore,
    AuditLogStore,
)
from guard_algorithms.models import SafetyResult


# ============================================================================
# 辅助：验证抽象类不可直接实例化
# ============================================================================

class TestAbstractInstantiation:
    @pytest.mark.parametrize("cls", [
        ModerationClient, NerRedactor, Embedder, ServerHistoryProvider,
        EntityExtractor, TrustLevelProvider, MemoryStore, AuditLogStore,
    ])
    def test_cannot_instantiate(self, cls):
        with pytest.raises(TypeError):
            cls()


# ============================================================================
# 验证具体实现可以正常实例化并调用
# ============================================================================

class TestModerationClient:
    def test_concrete_impl(self):
        class FakeModeration(ModerationClient):
            async def check(self, text: str) -> SafetyResult:
                return SafetyResult(is_safe=True)

        m = FakeModeration()
        assert isinstance(m, ModerationClient)


class TestNerRedactor:
    def test_concrete_impl(self):
        class FakeNer(NerRedactor):
            async def redact(self, text: str) -> tuple:
                return (text, ["PERSON"])

        n = FakeNer()
        assert isinstance(n, NerRedactor)


class TestEmbedder:
    def test_concrete_impl(self):
        class FakeEmbedder(Embedder):
            async def embed(self, text: str) -> list:
                return [0.1, 0.2, 0.3]

        e = FakeEmbedder()
        assert isinstance(e, Embedder)


class TestServerHistoryProvider:
    def test_concrete_impl(self):
        class FakeHistory(ServerHistoryProvider):
            async def get_history(self, session_id: str) -> list:
                return [("msg1", "hash1")]

        h = FakeHistory()
        assert isinstance(h, ServerHistoryProvider)


class TestEntityExtractor:
    def test_concrete_impl(self):
        class FakeExtractor(EntityExtractor):
            async def extract_entities(self, text: str) -> list:
                return ["entity1"]
            async def extract_keywords(self, text: str) -> list:
                return ["keyword1"]

        ex = FakeExtractor()
        assert isinstance(ex, EntityExtractor)


class TestTrustLevelProvider:
    def test_concrete_impl(self):
        class FakeTrust(TrustLevelProvider):
            async def get_trust_level(self, api_key: str) -> str:
                return "user"

        t = FakeTrust()
        assert isinstance(t, TrustLevelProvider)


class TestMemoryStore:
    def test_concrete_impl(self):
        class FakeMemory(MemoryStore):
            async def get(self, memory_id: str):
                return None
            async def update(self, memory_id: str, confidence: float):
                pass
            async def archive(self, memory_id: str, reason: str):
                pass

        ms = FakeMemory()
        assert isinstance(ms, MemoryStore)


class TestAuditLogStore:
    def test_concrete_impl(self):
        class FakeAudit(AuditLogStore):
            async def insert(self, entry):
                pass

        a = FakeAudit()
        assert isinstance(a, AuditLogStore)


# ============================================================================
# 验证缺少方法实现会报错
# ============================================================================

class TestPartialImplementation:
    def test_missing_method_fails(self):
        with pytest.raises(TypeError):
            class Incomplete(ModerationClient):
                pass
            Incomplete()
