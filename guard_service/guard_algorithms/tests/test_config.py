"""
config.py 自动化测试 —— GuardConfig 默认值 / 自定义覆盖 / 字段边界
"""

from guard_algorithms.config import GuardConfig


class TestGuardConfigDefaults:
    def test_strict_mode_default(self):
        c = GuardConfig()
        assert c.strict_mode is True

    def test_max_field_length_default(self):
        c = GuardConfig()
        assert c.max_field_length == 100

    def test_safe_charset_pattern_default(self):
        c = GuardConfig()
        assert r"\u4e00-\u9fa5" in c.safe_charset_pattern

    def test_regex_timeout_default(self):
        c = GuardConfig()
        assert c.regex_timeout_ms == 50.0

    def test_max_decode_rounds_default(self):
        c = GuardConfig()
        assert c.max_decode_rounds == 3

    def test_max_decoded_bytes_default(self):
        c = GuardConfig()
        assert c.max_decoded_bytes == 1024 * 1024

    def test_dangerous_patterns_default(self):
        c = GuardConfig()
        assert "忽略指令" in c.dangerous_patterns
        assert "jailbreak" in c.dangerous_patterns
        assert len(c.dangerous_patterns) >= 5

    def test_pii_patterns_default(self):
        c = GuardConfig()
        assert len(c.pii_patterns) >= 3  # 身份证、手机号、API Key

    def test_safe_rerank_threshold_default(self):
        c = GuardConfig()
        assert c.safe_rerank_threshold == 0.2

    def test_trust_weights_default(self):
        c = GuardConfig()
        assert c.trust_weights["system"] == 1.2
        assert c.trust_weights["user"] == 0.7
        assert c.trust_weights["unknown"] == 0.5

    def test_max_consecutive_drift_default(self):
        c = GuardConfig()
        assert c.max_consecutive_drift == 3

    def test_decay_rate_default(self):
        c = GuardConfig()
        assert c.decay_rate == 0.1

    def test_forget_threshold_default(self):
        c = GuardConfig()
        assert c.forget_threshold == 0.1

    def test_privilege_escalation_patterns_default(self):
        c = GuardConfig()
        assert len(c.privilege_escalation_patterns) >= 5

    def test_streaming_sample_rate_default(self):
        c = GuardConfig()
        assert c.streaming_sample_rate == 3

    def test_audit_snippet_length_default(self):
        c = GuardConfig()
        assert c.audit_snippet_length == 50


class TestGuardConfigCustom:
    def test_override_strict_mode(self):
        c = GuardConfig(strict_mode=False)
        assert c.strict_mode is False

    def test_override_max_field_length(self):
        c = GuardConfig(max_field_length=200)
        assert c.max_field_length == 200

    def test_override_dangerous_patterns(self):
        c = GuardConfig(dangerous_patterns=["test1", "test2"])
        assert c.dangerous_patterns == ["test1", "test2"]

    def test_override_trust_weights(self):
        c = GuardConfig(trust_weights={"system": 2.0, "user": 0.5})
        assert c.trust_weights["system"] == 2.0

    def test_override_safe_rerank_threshold(self):
        c = GuardConfig(safe_rerank_threshold=0.5)
        assert c.safe_rerank_threshold == 0.5

    def test_override_decay_rate(self):
        c = GuardConfig(decay_rate=0.3)
        assert c.decay_rate == 0.3

    def test_override_max_decode_rounds(self):
        c = GuardConfig(max_decode_rounds=5)
        assert c.max_decode_rounds == 5

    def test_multiple_overrides(self):
        c = GuardConfig(strict_mode=False, max_field_length=50, decay_rate=0.2)
        assert c.strict_mode is False
        assert c.max_field_length == 50
        assert c.decay_rate == 0.2


class TestGuardConfigIsolation:
    def test_default_list_isolation(self):
        """两个默认实例的列表不共享引用。"""
        c1 = GuardConfig()
        c2 = GuardConfig()
        c1.dangerous_patterns.append("new_pattern")
        assert "new_pattern" not in c2.dangerous_patterns

    def test_default_dict_isolation(self):
        """两个默认实例的字典不共享引用。"""
        c1 = GuardConfig()
        c2 = GuardConfig()
        c1.trust_weights["new_source"] = 1.0
        assert "new_source" not in c2.trust_weights
