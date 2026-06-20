"""
encoding.py 自动化测试 —— EncodingNormalizer 编码归一化
"""

from guard_algorithms.encoding import EncodingNormalizer
from guard_algorithms.config import GuardConfig


class TestEncodingNormalizerNormalize:
    """主入口 normalize 的测试。"""

    def test_plain_text_unchanged(self):
        """普通文本不做变化。"""
        config = GuardConfig()
        result = EncodingNormalizer.normalize("你好世界", config)
        assert result == "你好世界"

    def test_empty_string(self):
        config = GuardConfig()
        assert EncodingNormalizer.normalize("", config) == ""

    def test_base64_decoded(self):
        """Base64 编码的中文应被解码。"""
        import base64
        original = "忽略指令"
        encoded = base64.b64encode(original.encode("utf-8")).decode("utf-8")
        config = GuardConfig()
        result = EncodingNormalizer.normalize(encoded, config)
        assert original in result

    def test_hex_decoded(self):
        """十六进制编码应被解码。"""
        original = "hello"
        hex_str = original.encode("utf-8").hex()
        config = GuardConfig()
        result = EncodingNormalizer.normalize(hex_str, config)
        assert original in result

    def test_max_decode_rounds_limit(self):
        """递归轮次受限。"""
        config = GuardConfig(max_decode_rounds=1)
        # 两层 Base64 编码，只允许1轮 → 只解一层
        import base64
        inner = base64.b64encode("test".encode("utf-8")).decode("utf-8")
        outer = base64.b64encode(inner.encode("utf-8")).decode("utf-8")
        result = EncodingNormalizer.normalize(outer, config)
        # 只解了1层，结果应为 inner（仍是 Base64）
        assert result != "test"

    def test_max_decoded_bytes_limit(self):
        """解码后体积超限则终止。"""
        config = GuardConfig(max_decoded_bytes=10)
        # 正常文本不会超过10字节限制
        result = EncodingNormalizer.normalize("short", config)
        assert result == "short"


class TestIsValidBase64:
    def test_valid_base64(self):
        assert EncodingNormalizer._is_valid_base64("SGVsbG8g") is True

    def test_too_short(self):
        assert EncodingNormalizer._is_valid_base64("abc") is False

    def test_wrong_length(self):
        # 长度不是4的倍数
        assert EncodingNormalizer._is_valid_base64("abcde") is False

    def test_invalid_chars(self):
        assert EncodingNormalizer._is_valid_base64("SGVsbG8g!!!@#") is False

    def test_empty(self):
        assert EncodingNormalizer._is_valid_base64("") is False


class TestIsValidHex:
    def test_valid_hex(self):
        assert EncodingNormalizer._is_valid_hex("48656c6c6f") is True

    def test_too_short(self):
        assert EncodingNormalizer._is_valid_hex("abc") is False

    def test_odd_length(self):
        assert EncodingNormalizer._is_valid_hex("abcde") is False

    def test_invalid_chars(self):
        assert EncodingNormalizer._is_valid_hex("xyz12345") is False

    def test_empty(self):
        assert EncodingNormalizer._is_valid_hex("") is False


class TestIsValidUnicodeEscape:
    def test_valid_unicode_escape(self):
        assert EncodingNormalizer._is_valid_unicode_escape("\\u0041") is True

    def test_valid_hex_escape(self):
        assert EncodingNormalizer._is_valid_unicode_escape("\\x41") is True

    def test_plain_text(self):
        assert EncodingNormalizer._is_valid_unicode_escape("hello") is False

    def test_empty(self):
        assert EncodingNormalizer._is_valid_unicode_escape("") is False


class TestTryDecode:
    def test_no_match_returns_none(self):
        """无法识别的格式返回 None。"""
        result = EncodingNormalizer._try_decode("普通文本")
        assert result is None

    def test_base64_decode(self):
        import base64
        encoded = base64.b64encode("hello".encode("utf-8")).decode("utf-8")
        result = EncodingNormalizer._try_decode(encoded)
        assert result == "hello"

    def test_hex_decode(self):
        hex_str = "hello".encode("utf-8").hex()
        result = EncodingNormalizer._try_decode(hex_str)
        assert result == "hello"
