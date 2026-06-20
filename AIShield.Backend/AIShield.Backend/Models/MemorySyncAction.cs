using AIShield.Backend.Enums;

namespace AIShield.Backend.Models
{
    public class MemorySyncAction
    {
        // AIShield 数据库内部顺序主键。
        public long Id { get; set; }

        // 对外公开的幂等操作 ID。
        public Guid ActionId { get; set; } = Guid.NewGuid();

        // 动作所属 Agent。
        public long AgentId { get; set; }

        // 所属 Agent 导航属性。
        public AgentApp Agent { get; set; } = null!;

        // 用户主记忆库中的记忆 ID。
        public string ExternalMemoryId { get; set; } = string.Empty;

        // 用户 Agent 应执行的操作。
        public MemorySyncActionType ActionType { get; set; }

        // AIShield 计算后的置信度。
        public double? NewConfidence { get; set; }

        // 操作原因，例如 low_confidence、slot_conflict、manual_delete。
        public string? Reason { get; set; }

        // 当前同步状态。
        public MemorySyncActionStatus Status { get; set; }
            = MemorySyncActionStatus.Pending;

        // 动作创建时间。
        public DateTime CreatedAt { get; set; }

        // 用户 Agent 确认执行成功的时间。
        public DateTime? ConfirmedAt { get; set; }

        // 用户 Agent 报告的失败原因。
        public string? FailureMessage { get; set; }

        // 并发控制版本。
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // 用户 Agent 报告执行失败的时间。
        public DateTime? FailedAt { get; set; }
    }
}