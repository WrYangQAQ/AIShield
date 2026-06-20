"""
安全护栏算法库 —— Python 实现

接口约定：
  输入：request_data (dict) —— 从 HTTP 请求解析的 JSON Body / Query 参数
  输出：序列化 JSON 字符串（统一 ApiResponse 结构）

核心内容：
   输入侧：编码归一化 → 规则快筛（Aho-Corasick）→ 语义审核兜底（可插拔）
   工具调用：工具名白名单 + 参数结构化校验（长度/字符集）+ 注入特征扫描
   记忆写入：入库前脱敏（正则 + NER 可插拔）+ 入库前同等强度检测
   RAG 读取：相关性 × 信任权重 + 注入特征过滤
   会话完整性：服务端权威 history 对比，拒绝客户端伪造
   流式输出拦截：语义缓冲 → 安全检测 → 推流/中断
   主题漂移检测：实体/关键词匹配追踪
   来源隔离与信任分级：服务端权威 trust level + 提权攻击识别
   记忆衰减：基于正向引用的指数衰减
   审计日志：不记录明文，写入审计存储
"""

from .models import ApiResponse, SafetyResult, ValidationResult, MemoryEntry, AuditLogEntry
from .config import GuardConfig
from .interfaces import (
    ModerationClient,
    NerRedactor,
    Embedder,
    ServerHistoryProvider,
    EntityExtractor,
    TrustLevelProvider,
    MemoryStore,
    AuditLogStore,
)
from .core import GuardAlgorithms
from .encoding import EncodingNormalizer
from .text_utils import TextCanonicalizer, RegexCache
from .pii import PiiSanitizer
from .aho_corasick import AhoCorasickSearcher
from .local_embedder import LocalEmbedder
from .memory_conflict import detect_and_demote_conflicts

__all__ = [
    # 模型
    "ApiResponse", "SafetyResult", "ValidationResult", "MemoryEntry", "AuditLogEntry",
    # 配置
    "GuardConfig",
    # 接口
    "ModerationClient", "NerRedactor", "Embedder", "ServerHistoryProvider",
    "EntityExtractor", "TrustLevelProvider", "MemoryStore", "AuditLogStore",
    # 核心
    "GuardAlgorithms",
    # 工具
    "EncodingNormalizer", "TextCanonicalizer", "RegexCache", "PiiSanitizer",
    "AhoCorasickSearcher",
    # 本地 Embedder
    "LocalEmbedder",
    # 记忆冲突检测
    "detect_and_demote_conflicts",
]
