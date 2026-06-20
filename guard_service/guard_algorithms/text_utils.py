"""
文本归一化与正则缓存。

- TextCanonicalizer：规则匹配前的预处理（小写 + 压缩空白）
- RegexCache：正则编译缓存，避免每次请求重复编译
"""

from __future__ import annotations

import re
import threading


class TextCanonicalizer:
    """
    文本归一化 —— 规则匹配前的预处理。

    小写 + 压缩空白 + 去首尾空白。
    目的：减少大小写/空白变体导致的漏报。
    克制原则：不做强语义归一化（同形字、全角半角、拼音等），小项目保持克制。
    """

    _MULTI_WHITESPACE = re.compile(r"\s+")

    @staticmethod
    def canonicalize_for_match(text: str) -> str:
        """
        归一化文本用于规则匹配。

        Args:
            text: 原始文本

        Returns:
            归一化后的文本（小写、压缩空白、去首尾空白）
        """
        if not text:
            return ""
        return TextCanonicalizer._MULTI_WHITESPACE.sub(" ", text.lower()).strip()


class RegexCache:
    """
    正则编译缓存 —— 避免每次请求重复 new Regex。

    key 包含 pattern + timeout，防止不同超时复用错误实例。
    线程安全（使用锁保护缓存字典）。
    """

    _lock = threading.Lock()
    _cache: dict[str, re.Pattern[str]] = {}

    @staticmethod
    def get(pattern: str, timeout_ms: float = 50.0) -> re.Pattern[str]:
        """
        获取编译后的正则（带缓存）。

        Args:
            pattern: 正则模式
            timeout_ms: 超时毫秒数（Python re 不原生支持超时，仅用作缓存 key）

        Returns:
            编译后的 re.Pattern 对象
        """
        key = f"{pattern}\n{timeout_ms:.0f}"
        with RegexCache._lock:
            cached = RegexCache._cache.get(key)
            if cached is not None:
                return cached

            # Python 的 re 模块不直接支持 NonBacktracking，
            # 但我们可以用 re.ASCII | re.IGNORECASE 等标志。
            # 对于可能回溯的模式，建议用 regex 库替代。
            built = re.compile(pattern, re.ASCII)
            RegexCache._cache[key] = built
            return built
