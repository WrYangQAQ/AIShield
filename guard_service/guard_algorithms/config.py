"""
护栏配置 —— 所有阈值和规则集中管理，避免魔法数字散落在代码里。

使用方式：
  config = GuardConfig()                          # 默认值
  config = GuardConfig(max_field_length=200, strict_mode=False)  # 自定义

集成建议：
  生产环境建议从环境变量 / 配置文件加载，不要硬编码。
"""

from __future__ import annotations

from dataclasses import dataclass, field


@dataclass
class GuardConfig:
    """安全护栏全局配置。"""

    # ---- 通用 ----

    strict_mode: bool = True
    """
    严格优先（Fail-Closed）模式开关。
    True（默认）：关键依赖缺失/异常 → 阻断
    False：关键依赖缺失/异常 → 放行但标记 degraded
    ⚠️ 小项目不建议关闭严格模式
    """

    # ---- 结构化校验 ----

    max_field_length: int = 100
    """
    结构化字段最大长度（默认 100 字符）。
    防超长输入导致的资源消耗（DoS）和参数位注入载荷。
    """

    safe_charset_pattern: str = r"^[\u4e00-\u9fa5a-zA-Z0-9\s,.\-]+$"
    """
    结构化字段安全字符集正则。
    默认允许：中文、字母、数字、空格、逗号、句号、短横线。
    ⚠️ 只用于"明确应当结构化"的字段（如城市名、日期），不要对自由文本字段使用！
    """

    regex_timeout_ms: float = 50.0
    """
    正则超时（默认 50ms）。
    护栏位于请求入口，超时必须很短。超过 50-100ms 的正则建议改规则/换算法。
    """

    # ---- 编码归一化 ----

    max_decode_rounds: int = 3
    """
    编码归一化最大递归轮次（默认 3）。这是安全兜底，实际由 max_decoded_bytes 主控。
    正常攻击载荷 1-2 层编码就够了，3 层已经是极限场景。
    """

    max_decoded_bytes: int = 1024 * 1024
    """
    编码归一化解码后最大字节数（默认 1MB）。
    关键防御：短串多层递归解码可以膨胀到 MB 级，按层数限制防不住，必须按体积限制。
    """

    # ---- 规则快筛 ----

    dangerous_patterns: list[str] = field(default_factory=lambda: [
        # -- 中文注入锚点（短词组，覆盖常见变体） --
        "忽略之前",
        "忽略以上",
        "忽略前面",
        "忽略所有",
        "忽略指令",
        "忽略规则",
        "忽略限制",
        "无视之前",
        "无视以上",
        "忘记之前",
        "不要遵守",
        "不受限制",
        # -- 英文注入锚点 --
        "ignore previous",
        "ignore all instructions",
        "ignore all rules",
        "forget previous",
        "do not follow",
        # -- 系统提示词泄露 --
        "system prompt",
        "developer message",
        "show your prompt",
        "repeat your instructions",
        # -- 越狱关键词 --
        "jailbreak",
        "越狱",
        "dan",
        # -- 恶意行为锚点 --
        "木马",
        "后门",
        "勒索病毒",
        "恶意软件",
        "黑客入侵",
        "黑客攻击",
        "黑客工具",
        "提权",
        "漏洞利用",
        "肉鸡",
        "挖矿程序",
        "远程控制",
        "键盘记录",
        # -- 英文恶意行为 --
        "trojan",
        "backdoor",
        "ransomware",
        "malware",
        "rootkit",
        "keylogger",
        "exploit",
    ])
    """
    危险模式集合 —— 规则快筛的匹配词表。

    设计选择：用字面词条而非正则。
    原因：正则容易引发灾难性回溯（ReDoS），字面词条通过 Aho-Corasick
    多模式匹配，近线性时间，安全可控。复杂变体交给语义审核处理。

    运维建议：生产环境建议从外部配置加载，避免改代码重新部署。
    """

    # ---- PII 脱敏 ----

    pii_patterns: list[tuple[str, str]] = field(default_factory=lambda: [
        (r"\b\d{17}[\dXx]\b", "[ID_CARD_MASKED]"),
        (r"\b1[3-9]\d{9}\b", "[PHONE_MASKED]"),
        (r"(?:sk-|ak-)[A-Za-z0-9]{20,}", "[KEY_MASKED]"),
    ])
    """
    PII 脱敏规则 —— (正则模式, 替换掩码) 的列表。
    默认覆盖：身份证号、手机号、API Key。
    扩展建议：邮箱、银行卡、内部代号等按业务需要添加。
    """

    # ---- RAG 安全重排 ----

    safe_rerank_threshold: float = 0.2
    """
    RAG 安全重排阈值（默认 0.2）。
    过滤逻辑：相关性 × 信任权重 < 阈值 → 丢弃。
    0.2 较宽松，安全敏感场景可提高到 0.3-0.5。
    """

    trust_weights: dict[str, float] = field(default_factory=lambda: {
        "system": 1.2,
        "admin": 1.0,
        "user": 0.7,
        "unknown": 0.5,
    })
    """
    来源信任权重 —— 不同来源的文档在 RAG 重排时的加权系数。
    默认：system(1.2) > admin(1.0) > user(0.7) > unknown(0.5)。
    ⚠️ source 必须来自可信元数据（存储层/服务端标注），不能让文档内容自述！
    """

    # ---- 主题漂移 ----

    max_consecutive_drift: int = 3
    """
    主题漂移判定阈值 —— 连续多少个片段无锚点实体和关键词时判定为漂移（默认 3）。
    值越小越敏感（容易误报），值越大越宽松（可能漏报）。
    """

    # ---- 记忆衰减 ----

    decay_rate: float = 0.1
    """
    记忆衰减速率 —— 指数衰减模型中的 λ 参数（默认 0.1）。
    decay_factor = exp(-DecayRate × hours_since_ref)。
    值越大衰减越快：0.1 表示约10小时未引用 → 置信度降至37%。
    """

    forget_threshold: float = 0.1
    """
    记忆遗忘阈值 —— 置信度低于此值时触发归档（默认 0.1）。
    归档不是删除，而是标记为低优先级，不再参与常规召回。
    """

    # ---- 提权攻击检测 ----

    privilege_escalation_patterns: list[str] = field(default_factory=lambda: [
        "授予我管理员权限",
        "我是管理员",
        "提升我的权限",
        "忽略之前的信任级别",
        "system override",
        "grant me admin",
        "i am admin",
        "ignore trust level",
    ])
    """
    提权攻击检测模式 —— 用于识别"我是管理员"等提权尝试。
    使用字面词条 + Aho-Corasick 匹配，避免正则回溯。
    """

    # ---- 流式输出 ----

    streaming_sample_rate: int = 3
    """
    流式输出安全检测采样率（低风险区间）。
    默认 3：每 3 个语义完整缓冲块检测 1 次。
    高风险区间自动切换为逐块检测，不受此参数影响。
    """

    # ---- 审计日志 ----

    audit_snippet_length: int = 50
    """
    审计日志内容截断长度（默认 50 字符）。
    仅保留截断片段用于人工排查，不存完整明文。
    """

    # ---- 记忆冲突检测 ----

    conflict_similarity_threshold: float = 0.75
    """
    记忆冲突语义相似度阈值。新记忆与旧记忆余弦相似度 >= 此值且内容不同 → 视为意图替换。
    实测参考(bge-small-zh-v1.5)：学英语vs学德语≈0.80，学英语vs学Python≈0.62。
    0.75 可区分同领域冲突与跨领域不冲突。
    """

    conflict_penalty_weight: float = 0.6
    """
    冲突降权惩罚权重。new_confidence = old_confidence * (1 - penalty_weight)。0.6 表示降为原来的40%。
    """
