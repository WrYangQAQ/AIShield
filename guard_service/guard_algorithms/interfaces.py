"""
可插拔接口（集成方实现）—— 8 个 ABC 接口。

实现方只需实现对应接口的方法即可接入自定义的后端服务。
所有接口方法均为 async，超时/异常由本库统一处理。
"""

from __future__ import annotations

from abc import ABC, abstractmethod
from datetime import datetime
from typing import Optional

from .models import SafetyResult, MemoryEntry, AuditLogEntry


class ModerationClient(ABC):
    """
    语义审核接口 —— 规则快筛之后的兜底检测。

    为什么需要：规则只能匹配已知模式，语义审核能识别变体和语义层绕过。
    小项目建议：调用 OpenAI Moderation API 等云端服务，不要自己部署模型。
    如果暂无审核服务，传 None；严格模式下规则命中后会被阻断（Fail-Closed）。

    实现要求：
    - 必须使用 async/await
    - 超时/异常由本库统一处理，实现方不需要做熔断
    """

    @abstractmethod
    async def check(self, text: str) -> SafetyResult:
        """对文本执行安全审核，返回判定结果。"""
        ...


class NerRedactor(ABC):
    """
    NER/PII 脱敏接口（可选）。

    为什么需要：正则只能覆盖结构化 PII（身份证号、手机号），
    非结构化敏感信息（姓名+地址、病历）需要 NER 补充。
    传 None 则只用正则脱敏，不会报错。
    """

    @abstractmethod
    async def redact(self, text: str) -> tuple[str, list[str]]:
        """
        对文本进行脱敏。

        返回：(脱敏后文本, 命中的实体标签列表)
        """
        ...


class Embedder(ABC):
    """
    向量嵌入接口 —— RAG 安全重排用。

    关键约束：
    - query 只嵌入一次（本库保证在循环外调用）
    - doc embedding 必须在入库时预计算并持久化（否则查询时计算会成为瓶颈）
    """

    @abstractmethod
    async def embed(self, text: str) -> list[float]:
        """返回文本的向量嵌入。"""
        ...


class ServerHistoryProvider(ABC):
    """
    服务端权威历史提供接口 —— 会话完整性校验用。

    实现要求：
    - 必须来自服务端存储（Redis / DB），不能让客户端提交
    - 必须与鉴权后的 session 绑定一致
    """

    @abstractmethod
    async def get_history(self, session_id: str) -> list[tuple[str, str]]:
        """
        获取指定会话的服务端权威历史。

        返回：[(message_id, content_sha256_hash), ...] 的有序列表
        """
        ...


class EntityExtractor(ABC):
    """
    实体/关键词提取接口 —— 主题漂移检测用。

    为什么需要：check_topic_drift 需要从原始查询和生成片段中提取锚点，
    用来追踪各片段与原始意图的关联度。

    小项目建议：可用轻量级方案（jieba 分词 + 停用词过滤），不一定要上 NER 模型。
    传 None 则 CheckTopicDrift 会用内置的简单分词（按标点/空白切分 + 去停用词）。
    """

    @abstractmethod
    async def extract_entities(self, text: str) -> list[str]:
        """
        从文本中提取核心实体。

        返回：实体列表（去重、小写化）。
        """
        ...

    @abstractmethod
    async def extract_keywords(self, text: str) -> list[str]:
        """
        从文本中提取关键词。

        返回：关键词列表（去重、小写化）。
        """
        ...


class TrustLevelProvider(ABC):
    """
    信任等级提供接口 —— 来源隔离与信任分级用。

    实现要求：
    - 必须从服务端鉴权服务获取，不采信客户端/LLM 输出中的身份声明
    - API Key / Session Token → 服务端查库 → 返回真实 trust level
    - 信任等级绝不基于 Prompt 中的自我声明生效
    """

    @abstractmethod
    async def get_trust_level(self, api_key: str) -> str:
        """
        根据 API Key 或会话标识获取真实信任等级。

        返回：信任等级（system > admin > user > untrusted）
        """
        ...


class MemoryStore(ABC):
    """
    记忆存储接口 —— 记忆衰减用。

    实现要求：
    - get / update / archive 操作必须是原子的，避免并发读写导致数据不一致
    - last_positive_ref 为 None 表示从未被正向引用，应视为最久远（衰减最快）
    """

    @abstractmethod
    async def get(self, memory_id: str) -> Optional[MemoryEntry]:
        """获取记忆条目。不存在返回 None。"""
        ...

    @abstractmethod
    async def update(self, memory_id: str, confidence: float,
                     last_positive_ref: Optional[datetime] = None) -> None:
        """更新记忆条目的置信度，可选同时更新 last_positive_ref。

        Args:
            memory_id: 记忆ID
            confidence: 新的置信度
            last_positive_ref: 新的正向引用时间。为 None 时保持原值不变。
        """
        ...

    @abstractmethod
    async def archive(self, memory_id: str, reason: str) -> None:
        """归档/软删除记忆条目。"""
        ...


class AuditLogStore(ABC):
    """
    审计日志存储接口 —— 安全审计用。

    实现要求：
    - 写入操作应尽量轻量，不阻塞主请求链路
    - 不记录明文用户输入或解码结果，仅存 hash + 截断片段 + 风险标签 + requestId
    - 建议生产环境用异步队列（如 Kafka/RabbitMQ）写入，避免拖慢主链路
    """

    @abstractmethod
    async def insert(self, entry: AuditLogEntry) -> None:
        """写入一条审计日志。"""
        ...
