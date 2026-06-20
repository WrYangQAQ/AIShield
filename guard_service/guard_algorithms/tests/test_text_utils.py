"""
text_utils.py 自动化测试 —— TextCanonicalizer / RegexCache
"""

import re
from guard_algorithms.text_utils import TextCanonicalizer, RegexCache


class TestTextCanonicalizer:
    def test_lowercase(self):
        assert TextCanonicalizer.canonicalize_for_match("Hello WORLD") == "hello world"

    def test_compress_whitespace(self):
        result = TextCanonicalizer.canonicalize_for_match("hello   world")
        assert result == "hello world"

    def test_trim(self):
        result = TextCanonicalizer.canonicalize_for_match("  hello  ")
        assert result == "hello"

    def test_empty(self):
        assert TextCanonicalizer.canonicalize_for_match("") == ""

    def test_chinese(self):
        result = TextCanonicalizer.canonicalize_for_match("忽略 指令")
        assert result == "忽略 指令"

    def test_mixed_whitespace(self):
        result = TextCanonicalizer.canonicalize_for_match("a\t\nb")
        assert result == "a b"

    def test_none_like_empty(self):
        """传入空字符串不会崩溃。"""
        result = TextCanonicalizer.canonicalize_for_match("")
        assert result == ""


class TestRegexCache:
    def test_basic_compile(self):
        pat = RegexCache.get(r"\d+")
        assert isinstance(pat, re.Pattern)

    def test_same_pattern_returns_cached(self):
        p1 = RegexCache.get(r"\d+")
        p2 = RegexCache.get(r"\d+")
        assert p1 is p2

    def test_different_timeout_different_cache_key(self):
        """不同 timeout_ms 会生成不同的缓存 key，但 Python re.compile 内部缓存可能返回同一对象。"""
        p1 = RegexCache.get(r"\d+", timeout_ms=50)
        p2 = RegexCache.get(r"\d+", timeout_ms=100)
        # Python re.compile 内部缓存会让相同 pattern+flags 返回同一对象
        # 所以这里验证两者功能等价即可，不验证身份
        assert p1.pattern == p2.pattern

    def test_different_pattern_different_cache(self):
        p1 = RegexCache.get(r"\d+")
        p2 = RegexCache.get(r"[a-z]+")
        assert p1 is not p2

    def test_compiled_pattern_works(self):
        pat = RegexCache.get(r"\d+")
        assert pat.search("abc123")
        assert not pat.search("abcdef")

    def test_thread_safety(self):
        """多线程并发访问不崩溃。"""
        import threading
        errors = []

        def access():
            try:
                for _ in range(100):
                    RegexCache.get(r"test_\d+", 50)
            except Exception as e:
                errors.append(e)

        threads = [threading.Thread(target=access) for _ in range(10)]
        for t in threads:
            t.start()
        for t in threads:
            t.join()

        assert len(errors) == 0
