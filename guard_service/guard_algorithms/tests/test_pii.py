"""
pii.py 自动化测试 —— PiiSanitizer 正则脱敏 + NER 脱敏
"""

import pytest
from guard_algorithms.pii import PiiSanitizer
from guard_algorithms.config import GuardConfig
from guard_algorithms.interfaces import NerRedactor


class TestPiiSanitizerRegex:
    """正则脱敏测试。"""

    @pytest.mark.asyncio
    async def test_id_card_masked(self):
        """身份证号应被脱敏。"""
        config = GuardConfig()
        text = "我的身份证号是 110101199001011234"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config)
        assert "[ID_CARD_MASKED]" in sanitized
        assert "110101199001011234" not in sanitized
        assert error is False

    @pytest.mark.asyncio
    async def test_phone_masked(self):
        """手机号应被脱敏。"""
        config = GuardConfig()
        text = "联系我：13812345678"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config)
        assert "[PHONE_MASKED]" in sanitized
        assert "13812345678" not in sanitized
        assert error is False

    @pytest.mark.asyncio
    async def test_api_key_masked(self):
        """API Key 应被脱敏。"""
        config = GuardConfig()
        text = "key=sk-abcdefghijklmnopqrstuvwxyz1234"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config)
        assert "[KEY_MASKED]" in sanitized
        assert "sk-abcdefghijklmnopqrstuvwxyz1234" not in sanitized
        assert error is False

    @pytest.mark.asyncio
    async def test_no_pii(self):
        """无 PII 时原文不变。"""
        config = GuardConfig()
        text = "今天天气很好"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config)
        assert sanitized == text
        assert labels == []
        assert error is False

    @pytest.mark.asyncio
    async def test_empty_text(self):
        config = GuardConfig()
        sanitized, labels, error = await PiiSanitizer.sanitize("", config)
        assert sanitized == ""
        assert error is False


class TestPiiSanitizerNer:
    """NER 脱敏测试。"""

    @pytest.mark.asyncio
    async def test_ner_redact_success(self):
        """NER 脱敏正常工作。"""
        class FakeNer(NerRedactor):
            async def redact(self, text: str) -> tuple:
                return (text.replace("张三", "[NAME]"), ["PERSON"])

        config = GuardConfig()
        text = "张三今天来了"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config, FakeNer())
        assert "[NAME]" in sanitized
        assert "张三" not in sanitized
        assert "PERSON" in labels
        assert error is False

    @pytest.mark.asyncio
    async def test_ner_exception_strict(self):
        """NER 异常 + 严格模式 → 返回占位符。"""
        class BrokenNer(NerRedactor):
            async def redact(self, text: str) -> tuple:
                raise RuntimeError("NER down")

        config = GuardConfig(strict_mode=True)
        text = "hello"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config, BrokenNer())
        assert sanitized == "[PII_REDACTION_FAILED]"
        assert "pii_ner_error" in labels
        assert error is True

    @pytest.mark.asyncio
    async def test_ner_exception_non_strict(self):
        """NER 异常 + 非严格模式 → 返回当前脱敏结果。"""
        class BrokenNer(NerRedactor):
            async def redact(self, text: str) -> tuple:
                raise RuntimeError("NER down")

        config = GuardConfig(strict_mode=False)
        text = "hello"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config, BrokenNer())
        assert sanitized == "hello"  # 正则无命中，NER 异常后返回当前文本
        assert "pii_ner_error" in labels
        assert error is True

    @pytest.mark.asyncio
    async def test_no_ner_passes(self):
        """不传 NER 也能正常工作。"""
        config = GuardConfig()
        text = "普通文本"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config, ner_redactor=None)
        assert sanitized == text
        assert error is False


class TestPiiSanitizerRegexError:
    """正则超时/异常处理。"""

    @pytest.mark.asyncio
    async def test_custom_pii_patterns(self):
        """自定义 PII 规则生效。"""
        config = GuardConfig(pii_patterns=[
            (r"\b\d{6}\b", "[CODE_MASKED]"),
        ])
        text = "验证码 123456"
        sanitized, labels, error = await PiiSanitizer.sanitize(text, config)
        assert "[CODE_MASKED]" in sanitized
        assert "123456" not in sanitized
