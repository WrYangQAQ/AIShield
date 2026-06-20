using AIShield.Backend.Enums;
using System.ComponentModel.DataAnnotations;

namespace AIShield.Backend.Dtos
{
    public class GuardAlgorithmResult<TDetails>
    {
        // 算法是否正常完成并允许调用方继续执行。
        public bool Success { get; set; }

        // 机器可读的结果说明，例如 ok、archived、missing_query。
        public string Message { get; set; } = string.Empty;

        // 算法返回的数据主体。
        public GuardAlgorithmData<TDetails>? Data { get; set; }
    }

    public class GuardAlgorithmData<TDetails>
    {
        // 本次算法调用的追踪标识。
        public string RequestId { get; set; } = string.Empty;

        // 是否建议阻断当前业务流程。
        public bool Blocked { get; set; }

        // 可选的风险标签，例如 topic_drift、memory_not_found。
        public string? RiskLabel { get; set; }

        // 当前算法的详细结果。
        public TDetails? Details { get; set; }
    }

    public class MemoryWriteRequest
    {
        // 要进行写入前安全检测的记忆正文。
        public string Content { get; set; } = string.Empty;

        // 记忆来源标签，例如 user、admin、system。
        public MemorySource Source { get; set; } = MemorySource.User;    // todo:使用枚举，并且增加更多的Source值
    }

    public class MemoryWriteDetails
    {
        // 检测通过后允许写入的记忆正文。
        public string? ProcessedContent { get; set; }

        // 记忆来源标签。
        public MemorySource Source { get; set; }

        // 本次检测命中的本体安全规则编号。
        public List<string> HitRules { get; set; } = new();

        // 本次检测的原因说明。
        public string Reason { get; set; } = string.Empty;
    }

    public class MemoryPutRequest
    {
        // 记忆唯一标识；相同标识再次写入时执行更新。
        public string MemoryId { get; set; } = string.Empty;

        // 要保存的记忆正文。
        public string Content { get; set; } = string.Empty;

        // 初始置信度，范围为 0 到 1。
        public double Confidence { get; set; } = 1.0;

        // 记忆来源标签，例如 user、admin、system。
        public MemorySource Source { get; set; } = MemorySource.User;

        // 可选的业务槽位键；相同来源和槽位键的不同内容会被判定为确定冲突。
        public string? MemoryKey { get; set; }

        // 可选的向量嵌入；调用方已经生成向量时可直接传入，避免重复计算。
        public List<float>? Embedding { get; set; }

        // 最后一次正向引用时间，使用 ISO8601 格式；空字符串表示尚未被正向引用。
        public string LastPositiveRef { get; set; } = string.Empty;

        // 记忆在业务系统中的原始创建时间，使用 ISO8601 格式；为空时按当前时间处理。
        public string MemoryCreatedAt { get; set; } = string.Empty;
    }

    public class MemoryBulkPutRequest
    {
        // 待批量写入的记忆条目列表。
        public List<MemoryPutRequest> Memories { get; set; } = new();

        // 是否跳过冲突检测，纯历史导入场景可以设为 true。
        public bool SkipConflictCheck { get; set; }
    }

    public class MemoryDecayRequest
    {
        // 要执行置信度衰减的记忆条目 ID。
        public string MemoryId { get; set; } = string.Empty;
    }

    public class MemoryDetails
    {
        // 记忆唯一标识。
        public string MemoryId { get; set; } = string.Empty;

        // 当前置信度。
        public double Confidence { get; set; }

        // 记忆来源标签。
        public MemorySource Source { get; set; }

        // 可选的业务槽位键。
        public string? MemoryKey { get; set; }

        // 最后一次正向引用时间。
        public DateTime? LastPositiveRef { get; set; }

        // 当前执行的动作，例如 created、updated、decayed、archived。
        public string Action { get; set; } = string.Empty;

        // 与本次写入相关的冲突或相似记忆列表。
        public List<MemoryConflictDetails> Conflicts { get; set; } = new();

        // 记忆在业务系统中的原始创建时间。
        public DateTime MemoryCreatedAt { get; set; }
    }

    public class MemoryConflictDetails
    {
        // 被识别为冲突或相似项的旧记忆 ID。
        public string MemoryId { get; set; } = string.Empty;

        // 新旧记忆的相似度，范围为 0 到 1。
        public double Similarity { get; set; }

        // 冲突判定依据，例如 same_memory_key、similar_content_candidate。
        public string Reason { get; set; } = string.Empty;

        // 对旧记忆采取的动作，例如 demoted、archived、candidate。
        public string Action { get; set; } = string.Empty;

        // 旧记忆处理后的置信度；candidate 表示仅提示时保持原值。
        public double NewConfidence { get; set; }
    }

    public class MemoryBulkDetails
    {
        // 请求中的记忆总数。
        public int Total { get; set; }

        // 成功写入或更新的记忆数量。
        public int Succeeded { get; set; }

        // 写入失败的记忆数量。
        public int Failed { get; set; }

        // 检出的确定冲突与潜在冲突总数。
        public int Conflicts { get; set; }

        // 每条记忆的处理结果。
        public List<GuardAlgorithmResult<MemoryDetails>> Items { get; set; } = new();
    }

    public class MemoryReferenceRequest
    {
        // 记忆发生正向引用的时间，使用ISO8601格式
        public string ReferencedAt { get; set; } = string.Empty;
    }

    public class MemoryBatchDecayRequest
    {
        // 单次最多处理的记忆数量，避免一次加载过多记录。
        public int BatchSize { get; set; } = 200;

        // 可选：只衰减指定时间之前未处理过的记忆。
        public string? UpdatedBefore { get; set; }
    }

    public class MemoryBatchDecayDetails
    {
        // 本次实际处理的记忆数量。
        public int Processed { get; set; }

        // 置信度发生衰减但未归档的数量。
        public int Decayed { get; set; }

        // 因低置信度被归档的数量。
        public int Archived { get; set; }

        // 本批次处理后的记忆结果。
        public List<MemoryDetails> Items { get; set; } = new();
    }

    public class MemorySearchRequest : IValidatableObject
    {
        // 按外部记忆 ID 或业务槽位键进行模糊搜索。
        public string? Keyword { get; set; }

        // 按记忆来源筛选；为空表示不限。
        public MemorySource? Source { get; set; }

        // 是否归档；为空表示同时查询正常和归档记录。
        public bool? IsArchived { get; set; }

        // 最低置信度；为空表示不限。
        [Range(0, 1)]
        public double? MinConfidence { get; set; }

        // 最高置信度；为空表示不限。
        [Range(0, 1)]
        public double? MaxConfidence { get; set; }

        // 按归档原因筛选，例如 low_confidence、manual_delete。
        public string? ArchiveReason { get; set; }

        // 最后正向引用时间的开始范围。
        public DateTime? LastPositiveRefFrom { get; set; }

        // 最后正向引用时间的结束范围。
        public DateTime? LastPositiveRefTo { get; set; }

        // 排序字段：updatedAt、confidence、createdAt、lastPositiveRef。
        [RegularExpression(
            "^(updatedAt|confidence|createdAt|lastPositiveRef)$",
            ErrorMessage = "排序字段只能是 updatedAt、confidence、createdAt 或 lastPositiveRef。")]
        public string SortBy { get; set; } = "updatedAt";

        // 是否降序排列。
        public bool Descending { get; set; } = true;

        // 页码，从 1 开始。
        [Range(1, int.MaxValue)]
        public int PageIndex { get; set; } = 1;

        // 每页条数。
        [Range(1, 100)]
        public int PageSize { get; set; } = 20;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (MinConfidence.HasValue
                && MaxConfidence.HasValue
                && MinConfidence.Value > MaxConfidence.Value)
            {
                yield return new ValidationResult(
                    "最低置信度不能高于最高置信度。",
                    new[]
                    {
                nameof(MinConfidence),
                nameof(MaxConfidence)
                    });
            }

            if (LastPositiveRefFrom.HasValue
                && LastPositiveRefTo.HasValue
                && LastPositiveRefFrom.Value > LastPositiveRefTo.Value)
            {
                yield return new ValidationResult(
                    "最后引用时间的开始值不能晚于结束值。",
                    new[]
                    {
                nameof(LastPositiveRefFrom),
                nameof(LastPositiveRefTo)
                    });
            }

            if (Keyword?.Length > 200)
            {
                yield return new ValidationResult(
                    "搜索关键词长度不能超过 200。",
                    new[] { nameof(Keyword) });
            }

            if (ArchiveReason?.Length > 100)
            {
                yield return new ValidationResult(
                    "归档原因长度不能超过 100。",
                    new[] { nameof(ArchiveReason) });
            }
        }
    }

    public class MemoryListItem
    {
        // 用户 Agent 记忆库中的原始记忆 ID。
        public string MemoryId { get; set; } = string.Empty;

        // 记忆来源。
        public MemorySource Source { get; set; }

        // 可选的业务槽位键。
        public string? MemoryKey { get; set; }

        // 当前置信度。
        public double Confidence { get; set; }

        // 记忆在业务系统中的原始创建时间。
        public DateTime MemoryCreatedAt { get; set; }

        // 最后一次正向引用时间。
        public DateTime? LastPositiveRef { get; set; }

        // 最后一次执行衰减的时间。
        public DateTime? LastDecayAt { get; set; }

        // 是否已经归档。
        public bool IsArchived { get; set; }

        // 归档原因。
        public string? ArchiveReason { get; set; }

        // 同步到 AIShield 的时间。
        public DateTime CreatedAt { get; set; }

        // AIShield 中最后更新时间。
        public DateTime UpdatedAt { get; set; }
    }

    public class MemoryRestoreRequest
    {
        // 恢复后的置信度；为空时保留原置信度。
        [Range(0, 1)]
        public double? Confidence { get; set; }

        // 恢复确认时间，使用 ISO8601 格式；为空时使用服务器当前时间。
        public string ReferencedAt { get; set; } = string.Empty;
    }

    public class MemorySyncActionQueryRequest
    {
        // 单次最多拉取的待同步动作数量。
        [Range(1, 200)]
        public int Limit { get; set; } = 100;
    }

    public class MemorySyncActionItem
    {
        // 对外公开的幂等操作 ID。
        public Guid ActionId { get; set; }

        // 用户主记忆库中的记忆 ID。
        public string MemoryId { get; set; } = string.Empty;

        // 用户 Agent 应执行的动作。
        public MemorySyncActionType ActionType { get; set; }

        // AIShield 计算后的最新置信度。
        public double? NewConfidence { get; set; }

        // 动作产生原因。
        public string? Reason { get; set; }

        // 动作创建时间。
        public DateTime CreatedAt { get; set; }
    }

    public class MemorySyncFailureRequest
    {
        // 用户 Agent 执行同步动作失败的原因。
        [Required]
        [StringLength(500)]
        public string FailureMessage { get; set; } = string.Empty;
    }

    public class MemorySyncActionResult
    {
        // 被确认或标记失败的操作 ID。
        public Guid ActionId { get; set; }

        // 当前同步状态。
        public MemorySyncActionStatus Status { get; set; }

        // 状态更新时间。
        public DateTime UpdatedAt { get; set; }
    }

    public class RagRerankRequest
    {
        // 用户当前查询文本。
        public string Query { get; set; } = string.Empty;

        // 可选的查询向量；提供后可以执行语义相似度计算。
        public List<float>? QueryEmbedding { get; set; }

        // 待重排的候选文档列表。
        public List<RagRerankCandidate> Candidates { get; set; } = new();

        // 可选的最低保留分数；为空时使用服务端配置。
        public double? MinimumScore { get; set; }

        // 最多返回的候选数量。
        public int MaxResults { get; set; } = 20;

        // 调用方或 Agent 标识。
        public string AgentKey { get; set; } = "rag-service";
    }

    public class RagRerankCandidate
    {
        // 候选文档唯一标识。
        public string Id { get; set; } = string.Empty;

        // 候选文档正文。
        public string Content { get; set; } = string.Empty;

        // 可选的候选文档向量，应在入库时预先生成。
        public List<float>? Embedding { get; set; }

        // 候选文档来源标签，例如 system、admin、user、unknown。
        public string Source { get; set; } = "unknown";
    }

    public class RagRerankDetails
    {
        // 实际采用的重排模式，例如 hybrid、lexical_fallback。
        public string Mode { get; set; } = string.Empty;       // todo：重构为枚举类型

        // 过滤掉的危险候选数量。
        public int FilteredUnsafeCount { get; set; }

        // 按最终分数降序排列的保留结果。
        public List<RagRerankItem> Kept { get; set; } = new();
    }

    public class RagRerankItem
    {
        // 候选文档唯一标识。
        public string Id { get; set; } = string.Empty;

        // 候选文档正文。
        public string Content { get; set; } = string.Empty;

        // 候选文档来源标签。
        public string Source { get; set; } = string.Empty;

        // 综合语义相关性、文本相关性和来源信任度后的最终分数。
        public double Score { get; set; }

        // 向量语义相似度；未提供有效向量时为空。
        public double? SemanticScore { get; set; }

        // 基于词元和字符片段计算的文本相关性。
        public double LexicalScore { get; set; }
    }

    public class TopicDriftRequest
    {
        // 原始用户问题，用于建立主题锚点。
        public string Query { get; set; } = string.Empty;

        // 后续生成或对话片段列表，按生成顺序检测。
        public List<string> Segments { get; set; } = new();

        // 可选的连续低相关片段阈值；为空时使用服务端配置。
        public int? MaxConsecutiveDrift { get; set; }

        // 可选的片段最低相关度；为空时使用服务端配置。
        public double? SimilarityThreshold { get; set; }

        // 调用方或 Agent 标识。
        public string AgentKey { get; set; } = "ai-chat";
    }

    public class TopicDriftDetails
    {
        // 是否检测到主题漂移。
        public bool Drifted { get; set; }

        // 结束检测时连续低相关片段数量。
        public int DriftCount { get; set; }

        // 本次使用的连续低相关片段阈值。
        public int MaxConsecutiveDrift { get; set; }

        // 从原始问题中提取出的主题锚点。
        public List<string> Anchors { get; set; } = new();

        // 每个片段的相关度和判定明细。
        public List<TopicSegmentScore> Segments { get; set; } = new();
    }

    public class TopicSegmentScore
    {
        // 片段在请求列表中的索引。
        public int Index { get; set; }

        // 片段与原始问题的综合相关度。
        public double Score { get; set; }

        // 片段是否被判定为低相关。
        public bool LowRelevance { get; set; }
    }
}
