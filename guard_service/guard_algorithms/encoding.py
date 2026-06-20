"""
编码与格式归一化 —— 白名单驱动的递归解码。

对应文档：(一) → 3. 编码与格式归一化

目的：让"被编码隐藏的注入载荷"在检测前还原为可匹配文本，降低编码绕过。

安全设计：
  - 只解码有明确 RFC 标准的编码格式（Base64/Hex/Unicode escape）
  - 解码失败 ≠ 安全：失败时保留原文继续走后续检测
  - max_decoded_bytes 防止递归解码膨胀型 DoS（短串多层递归可膨胀至 MB 级）
  - 避免使用复杂正则（减少 ReDoS 攻击面）
"""

from __future__ import annotations

import base64
from typing import Optional

from .config import GuardConfig


class EncodingNormalizer:
    """
    编码与格式归一化器。

    递归逻辑：
      每轮尝试 Base64 → Hex → Unicode escape 依次解码，
      命中第一个成功解码的格式就用其结果继续下一轮，
      全部失败或无变化则终止。

    为什么需要递归：攻击者可能用 Base64(Hex(Unicode("忽略指令"))) 三层嵌套。
    """

    @staticmethod
    def normalize(raw_text: str, config: GuardConfig) -> str:
        """
        编码归一化主入口。

        递归解码直到无法继续或超限。

        Args:
            raw_text: 原始输入文本
            config: 护栏配置

        Returns:
            归一化后的文本
        """
        current = raw_text

        # 最多递归 max_decode_rounds 轮（安全兜底，实际由 max_decoded_bytes 主控）
        for _ in range(max(1, config.max_decode_rounds)):
            decoded = EncodingNormalizer._try_decode(current)

            # 无法识别 / 无变化 → 终止
            if decoded is None or decoded == current:
                break

            # 解码后体积超限 → 终止（主防 DoS：短串多层递归可膨胀至 MB 级）
            if len(decoded.encode("utf-8")) > config.max_decoded_bytes:
                break

            current = decoded

        return current

    @staticmethod
    def _try_decode(text: str) -> Optional[str]:
        """依次尝试 Base64 → Hex → Unicode escape 解码，返回第一个成功的结果。"""
        if EncodingNormalizer._is_valid_base64(text):
            result = EncodingNormalizer._try_decode_base64(text)
            if result is not None:
                return result

        if EncodingNormalizer._is_valid_hex(text):
            result = EncodingNormalizer._try_decode_hex(text)
            if result is not None:
                return result

        if EncodingNormalizer._is_valid_unicode_escape(text):
            result = EncodingNormalizer._try_decode_unicode_escape(text)
            if result is not None:
                return result

        return None

    # ---- Base64 ----

    @staticmethod
    def _is_valid_base64(text: str) -> bool:
        """
        判断是否看起来像有效 Base64。
        依据：长度≥8、长度是4的倍数、字符集只含 [A-Za-z0-9+/=] 和空白。
        不用正则，避免 ReDoS。
        """
        s = text.strip()
        if len(s) < 8 or len(s) % 4 != 0:
            return False
        for ch in s:
            if not (ch.isalnum() or ch in ("+", "/", "=", " ", "\t", "\r", "\n")):
                return False
        return True

    @staticmethod
    def _try_decode_base64(text: str) -> Optional[str]:
        """尝试 Base64 解码，失败返回 None。"""
        try:
            # 移除空白
            cleaned = "".join(text.split())
            return base64.b64decode(cleaned).decode("utf-8")
        except Exception:
            return None

    # ---- Hex ----

    @staticmethod
    def _is_valid_hex(text: str) -> bool:
        """
        判断是否看起来像有效十六进制。
        依据：长度≥8、长度是2的倍数、字符集只含 [0-9a-fA-F] 和空白。
        """
        s = text.strip()
        if len(s) < 8 or len(s) % 2 != 0:
            return False
        for ch in s:
            if not (ch in "0123456789abcdefABCDEF" or ch in (" ", "\t", "\r", "\n")):
                return False
        return True

    @staticmethod
    def _try_decode_hex(text: str) -> Optional[str]:
        """尝试十六进制解码，失败返回 None。"""
        try:
            cleaned = "".join(ch for ch in text if ch not in " \t\r\n")
            return bytes.fromhex(cleaned).decode("utf-8")
        except Exception:
            return None

    # ---- Unicode escape ----

    @staticmethod
    def _is_valid_unicode_escape(text: str) -> bool:
        """判断是否包含 Unicode 转义序列（\\uXXXX 或 \\xXX）。"""
        if not text:
            return False
        for i in range(len(text) - 1):
            if text[i] != "\\":
                continue
            if text[i + 1] == "u" and i + 5 < len(text):
                return True
            if text[i + 1] == "x" and i + 3 < len(text):
                return True
        return False

    @staticmethod
    def _try_decode_unicode_escape(text: str) -> Optional[str]:
        """
        尝试 Unicode 转义解码。

        处理 \\uXXXX 和 \\xXX 两种格式，与原文相同返回 None。
        """
        try:
            # 使用 codecs.decode 处理 unicode-escape
            import codecs
            decoded = codecs.decode(text, "unicode-escape")
            return None if decoded == text else decoded
        except Exception:
            return None
