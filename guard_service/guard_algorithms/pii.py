"""
PII 脱敏：正则（结构化 PII）+ NER（非结构化，可选）。

对应文档：(二) → 1. 写入前净化 → 3) PII与敏感信息脱敏

返回三元组：
  - sanitized：脱敏后文本
  - labels：命中标签（不含明文，用于审计）
  - error：是否发生异常（严格模式下导致上游阻断）
"""

from __future__ import annotations

from typing import Optional

from .config import GuardConfig
from .interfaces import NerRedactor
from .text_utils import RegexCache


class PiiSanitizer:
    """
    PII 脱敏器。

    流程：正则脱敏 → NER 脱敏（可选）。
    正则超时/NER 异常 → 严格模式返回占位符，非严格模式返回当前文本。
    """

    @staticmethod
    async def sanitize(
        text: str,
        config: GuardConfig,
        ner_redactor: Optional[NerRedactor] = None,
    ) -> tuple[str, list[str], bool]:
        """
        异步脱敏。

        Args:
            text: 待脱敏文本
            config: 护栏配置
            ner_redactor: 可选的 NER 脱敏器

        Returns:
            (脱敏后文本, 命中标签列表, 是否出错)
            - sanitized: 脱敏后文本
            - labels: 命中标签（不含明文，用于审计）
            - error: 是否发生异常（严格模式下导致上游阻断）
        """
        sanitized = text
        labels: list[str] = []

        # 第一步：正则脱敏（结构化 PII：身份证、手机号、API Key）
        for pattern, mask in config.pii_patterns:
            rx = RegexCache.get(pattern, config.regex_timeout_ms)
            try:
                new_sanitized = rx.sub(mask, sanitized)
                # 如果文本被替换了，说明命中了这条规则，记录标签
                if new_sanitized != sanitized:
                    labels.append(mask)
                sanitized = new_sanitized
            except Exception:
                if config.strict_mode:
                    return "[PII_REDACTION_TIMEOUT]", ["pii_regex_timeout"], True
                return sanitized, ["pii_regex_timeout"], True

        # 第二步：NER 脱敏（可选，补正则覆盖不到的）
        if ner_redactor is None:
            return sanitized, labels, False

        try:
            redacted, entity_labels = await ner_redactor.redact(sanitized)
            labels.extend(entity_labels)
            return redacted, labels, False
        except Exception:
            if config.strict_mode:
                return "[PII_REDACTION_FAILED]", ["pii_ner_error"], True
            return sanitized, ["pii_ner_error"], True
