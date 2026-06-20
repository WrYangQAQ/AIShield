using AIShield.Backend.Enums;

namespace AIShield.Backend.Models
{
    public class MemoryRecord
    {
        // 记忆唯一标识，由业务侧生成并保持稳定。
        public long Id { get; set; }

        // 记忆所属的 Agent
        public long AgentId { get; set; }

        // 用户 Agent 记忆库中的原始记忆 ID。
        public string ExternalMemoryId { get; set; } = string.Empty;

        // 导航属性：记忆所属agent
        public AgentApp Agent { get; set; } = null!;

        // 记忆正文，仅在需要参与召回或冲突判断时读取。
        public string Content { get; set; } = string.Empty;

        // 记忆正文的 SHA256 哈希，用于快速识别完全重复内容。
        public string ContentHash { get; set; } = string.Empty;

        // 记忆来源标签，例如 user、admin、system。
        public MemorySource Source { get; set; } = MemorySource.User;

        // 可选的业务槽位键，例如 preferred_language；相同槽位的新旧值才视为确定冲突。
        public string? MemoryKey { get; set; }

        // 当前置信度，范围为 0 到 1。
        public double Confidence { get; set; } = 1.0;

        // JSON 格式的向量嵌入；为空时使用文本相似度完成降级判断。
        public string? EmbeddingJson { get; set; }

        // 记忆在用户 Agent 主记忆库中的原始创建时间。
        public DateTime MemoryCreatedAt { get; set; }

        // 最后一次正向引用时间；正向引用会暂停此前时间段的衰减累计。
        public DateTime? LastPositiveRef { get; set; }

        // 最后一次执行衰减计算的时间，用于保证重复调度不会对同一时间段重复衰减。
        public DateTime? LastDecayAt { get; set; }

        // 记忆创建时间。
        public DateTime CreatedAt { get; set; }

        // 记忆最后更新时间。
        public DateTime UpdatedAt { get; set; }

        // 是否已经软归档；归档记录不再参与普通召回和冲突检测。
        public bool IsArchived { get; set; }

        // 归档原因，例如 manual_delete、low_confidence、slot_conflict。
        public string? ArchiveReason { get; set; }

        // 数据库并发版本，防止并行衰减或更新覆盖彼此结果。
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    }
}
