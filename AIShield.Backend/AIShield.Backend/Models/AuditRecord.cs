using AIShield.Backend.Enums;

namespace AIShield.Backend.Models
{
    public class AuditRecord
    {
        // 审计记录数据库主键
        public long Id { get; set; }

        // 产生审计记录的 Agent 主键
        public long AgentId { get; set; }

        // Agent 名称快照，便于日志展示
        public string AgentName { get; set; } = string.Empty;

        // 可选的业务侧主体哈希，不保存用户明文标识
        public string? SubjectHash { get; set; }

        // 记录创建时间
        public DateTime CreatedAt { get; set; }

        // 检测方向
        public AuditDirection Direction { get; set; }

        // 原始输入、输出或工具调用内容
        public string OriginalContent { get; set; } = string.Empty;

        // 处理后的内容，例如脱敏后的输出
        public string? ProcessedContent { get; set; }

        // 本次检测评估出的风险等级
        public RiskLevel RiskLevel { get; set; }

        // 本次检测采取的处理动作
        public SecurityAction Action { get; set; }

        // 命中的规则编号集合
        public string HitRules { get; set; } = string.Empty;

        // 检测结果说明
        public string Reason { get; set; } = string.Empty;

        // 调用方客户端 IP 地址
        public string ClientIp { get; set; } = string.Empty;

        // 请求执行耗时，单位毫秒
        public long DurationMs { get; set; }
    }
}
