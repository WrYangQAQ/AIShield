"""
models.py 自动化测试 —— ApiResponse / SafetyResult / ValidationResult / MemoryEntry / AuditLogEntry
"""

import json
import pytest
from datetime import datetime, timezone

from guard_algorithms.models import (
    ApiResponse,
    SafetyResult,
    ValidationResult,
    MemoryEntry,
    AuditLogEntry,
    _snake_to_camel,
    _to_camel_dict,
    _none_skip_factory,
)


# ============================================================================
# _snake_to_camel
# ============================================================================

class TestSnakeToCamel:
    def test_single_word(self):
        assert _snake_to_camel("success") == "success"

    def test_two_words(self):
        assert _snake_to_camel("risk_label") == "riskLabel"

    def test_three_words(self):
        assert _snake_to_camel("last_positive_ref") == "lastPositiveRef"

    def test_no_underscore(self):
        assert _snake_to_camel("ok") == "ok"

    def test_empty(self):
        assert _snake_to_camel("") == ""


# ============================================================================
# _none_skip_factory
# ============================================================================

class TestNoneSkipFactory:
    def test_skip_none(self):
        items = [("a", 1), ("b", None), ("c", "x")]
        result = _none_skip_factory(items)
        assert result == {"a": 1, "c": "x"}

    def test_all_none(self):
        items = [("a", None), ("b", None)]
        result = _none_skip_factory(items)
        assert result == {}

    def test_no_none(self):
        items = [("a", 1), ("b", 2)]
        result = _none_skip_factory(items)
        assert result == {"a": 1, "b": 2}


# ============================================================================
# _to_camel_dict
# ============================================================================

class TestToCamelDict:
    def test_nested_dict(self):
        d = {"risk_label": "x", "inner_key": {"deep_value": 1}}
        result = _to_camel_dict(d)
        assert result == {"riskLabel": "x", "innerKey": {"deepValue": 1}}

    def test_list_of_dicts(self):
        d = {"items": [{"item_name": "a"}, {"item_name": "b"}]}
        result = _to_camel_dict(d)
        assert result == {"items": [{"itemName": "a"}, {"itemName": "b"}]}

    def test_non_dict_passthrough(self):
        assert _to_camel_dict("hello") == "hello"
        assert _to_camel_dict(42) == 42


# ============================================================================
# ApiResponse
# ============================================================================

class TestApiResponse:
    def test_to_json_basic(self):
        resp = ApiResponse(success=True, message="ok")
        data = json.loads(resp.to_json())
        assert data["success"] is True
        assert data["message"] == "ok"
        assert "data" not in data  # None 被 skip

    def test_to_json_with_data(self):
        resp = ApiResponse(success=True, message="ok", data={"requestId": "abc", "blocked": False})
        data = json.loads(resp.to_json())
        assert data["data"]["requestId"] == "abc"
        assert data["data"]["blocked"] is False

    def test_to_json_camel_case(self):
        resp = ApiResponse(success=False, message="blocked", data={"riskLabel": "injection"})
        j = resp.to_json()
        data = json.loads(j)
        assert "riskLabel" in data["data"]

    def test_to_json_chinese(self):
        resp = ApiResponse(success=True, message="ok", data={"reason": "中文测试"})
        j = resp.to_json()
        assert "中文测试" in j  # ensure_ascii=False

    def test_blocked_response(self):
        resp = ApiResponse(
            success=False, message="blocked",
            data={"requestId": "r1", "blocked": True, "riskLabel": "injection_attempt"},
        )
        data = json.loads(resp.to_json())
        assert data["success"] is False
        assert data["data"]["blocked"] is True

    def test_data_none_omitted(self):
        resp = ApiResponse(success=True, message="ok", data=None)
        j = resp.to_json()
        data = json.loads(j)
        assert "data" not in data

    def test_data_with_nested_none(self):
        """嵌套 dict 中的 None 不会被 _none_skip_factory 跳过（它只处理顶层字段）。"""
        resp = ApiResponse(success=True, message="ok", data={"riskLabel": None, "blocked": False})
        data = json.loads(resp.to_json())
        # None 在嵌套 dict 中保留，因为 _none_skip_factory 只作用于 asdict 的顶层字段
        assert "riskLabel" in data["data"]
        assert data["data"]["riskLabel"] is None
        assert data["data"]["blocked"] is False


# ============================================================================
# SafetyResult
# ============================================================================

class TestSafetyResult:
    def test_safe(self):
        r = SafetyResult(is_safe=True)
        assert r.is_safe is True
        assert r.risk_label is None

    def test_unsafe_with_label(self):
        r = SafetyResult(is_safe=False, risk_label="injection")
        assert r.is_safe is False
        assert r.risk_label == "injection"

    def test_frozen(self):
        r = SafetyResult(is_safe=True)
        with pytest.raises(AttributeError):
            r.is_safe = False


# ============================================================================
# ValidationResult
# ============================================================================

class TestValidationResult:
    def test_valid(self):
        v = ValidationResult(valid=True)
        assert v.valid is True
        assert v.reason is None

    def test_invalid(self):
        v = ValidationResult(valid=False, reason="too_long")
        assert v.valid is False
        assert v.reason == "too_long"

    def test_frozen(self):
        v = ValidationResult(valid=True)
        with pytest.raises(AttributeError):
            v.valid = False


# ============================================================================
# MemoryEntry
# ============================================================================

class TestMemoryEntry:
    def test_defaults(self):
        m = MemoryEntry()
        assert m.id == ""
        assert m.confidence == 0.0
        assert m.last_positive_ref is None
        assert m.source == "unknown"

    def test_custom(self):
        now = datetime.now(timezone.utc)
        m = MemoryEntry(id="mem-1", content="test", confidence=0.8,
                        last_positive_ref=now, source="system")
        assert m.id == "mem-1"
        assert m.confidence == 0.8
        assert m.last_positive_ref == now


# ============================================================================
# AuditLogEntry
# ============================================================================

class TestAuditLogEntry:
    def test_defaults(self):
        e = AuditLogEntry()
        assert e.request_id == ""
        assert e.event_type == ""
        assert e.content_hash == ""
        assert e.content_snippet is None
        assert e.risk_labels == []
        assert e.action_taken == ""
        assert isinstance(e.timestamp, datetime)

    def test_custom(self):
        e = AuditLogEntry(
            request_id="r1", event_type="injection",
            content_hash="abc123", content_snippet="忽略...",
            risk_labels=["injection"], action_taken="BLOCKED",
        )
        assert e.request_id == "r1"
        assert e.action_taken == "BLOCKED"
