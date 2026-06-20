"""
__init__.py 自动化测试 —— 包入口统一导出校验
"""


class TestPackageImports:
    def test_import_guard_algorithms(self):
        import guard_algorithms
        assert guard_algorithms is not None

    def test_import_guard_algorithms_class(self):
        from guard_algorithms import GuardAlgorithms
        assert GuardAlgorithms is not None

    def test_import_config(self):
        from guard_algorithms import GuardConfig
        assert GuardConfig is not None

    def test_import_models(self):
        from guard_algorithms import ApiResponse, SafetyResult, ValidationResult
        assert ApiResponse is not None
        assert SafetyResult is not None
        assert ValidationResult is not None

    def test_import_memory_entry(self):
        from guard_algorithms import MemoryEntry
        assert MemoryEntry is not None

    def test_import_audit_log_entry(self):
        from guard_algorithms import AuditLogEntry
        assert AuditLogEntry is not None

    def test_import_interfaces(self):
        from guard_algorithms import (
            ModerationClient, NerRedactor, Embedder,
            ServerHistoryProvider, EntityExtractor,
            TrustLevelProvider, MemoryStore, AuditLogStore,
        )
        assert all([
            ModerationClient, NerRedactor, Embedder,
            ServerHistoryProvider, EntityExtractor,
            TrustLevelProvider, MemoryStore, AuditLogStore,
        ])

    def test_import_encoding(self):
        from guard_algorithms import EncodingNormalizer
        assert EncodingNormalizer is not None

    def test_import_text_utils(self):
        from guard_algorithms import TextCanonicalizer, RegexCache
        assert TextCanonicalizer is not None
        assert RegexCache is not None

    def test_import_pii(self):
        from guard_algorithms import PiiSanitizer
        assert PiiSanitizer is not None

    def test_import_aho_corasick(self):
        from guard_algorithms import AhoCorasickSearcher
        assert AhoCorasickSearcher is not None


class TestPackageCreateInstances:
    def test_create_guard_algorithms(self):
        from guard_algorithms import GuardAlgorithms
        guard = GuardAlgorithms()
        assert guard is not None

    def test_create_config(self):
        from guard_algorithms import GuardConfig
        config = GuardConfig()
        assert config.strict_mode is True

    def test_create_api_response(self):
        from guard_algorithms import ApiResponse
        resp = ApiResponse(success=True, message="ok")
        assert resp.success is True

    def test_create_aho_corasick(self):
        from guard_algorithms import AhoCorasickSearcher
        ac = AhoCorasickSearcher(["test"])
        hit, matched = ac.search("test value")
        assert hit is True
