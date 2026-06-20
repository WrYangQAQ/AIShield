using AIShield.Backend.Enums;

namespace AIShield.Backend.Dtos
{
    public class SecurityCheckResponse
    {
        // 请求是否允许继续执行
        public bool Allowed { get; set; }

        // 后端建议执行的处理动作
        public SecurityAction Action { get; set; }

        // 检测得到的最高风险等级
        public RiskLevel RiskLevel { get; set; }

        // 处理后的内容，输出脱敏场景会返回替换后的文本
        public string? ProcessedContent { get; set; }

        // 处理原因说明
        public string Reason { get; set; } = string.Empty;

        // 命中的规则编号集合
        public List<string> HitRules { get; set; } = new();
    }
}
