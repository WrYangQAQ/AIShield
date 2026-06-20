using AIShield.Backend.Enums;

namespace AIShield.Backend.Dtos
{
    public class AuditSearchRequest
    {
        // 当前 AgentId；为空表示全局模式，查询全部 Agent
        public long? AgentId { get; set; }

        // 事件类型：Input / Output / ToolCall；为空表示不限
        public AuditDirection? Direction { get; set; }

        // 风险等级：None / Low / Medium / High / Critical；为空表示不限
        public RiskLevel? RiskLevel { get; set; }

        // 处理动作：Allow / Warn / Block / Mask / NeedApproval；为空表示不限
        public SecurityAction? Action { get; set; }

        // 命中规则编号，例如 PI001；为空表示不限
        public string? HitRule { get; set; }

        // 关键词，匹配原因、原始内容、处理后内容
        public string? Keyword { get; set; }

        // 开始时间
        public DateTime? StartTime { get; set; }

        // 结束时间
        public DateTime? EndTime { get; set; }

        // 页码，从 1 开始
        public int PageIndex { get; set; } = 1;

        // 每页条数
        public int PageSize { get; set; } = 20;
    }
}
