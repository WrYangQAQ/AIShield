namespace AIShield.Backend.Enums
{
    public enum RiskLevel
    {
        // 未检测到安全风险
        None = 0,

        // 低风险，通常只需要记录或轻度关注
        Low = 1,

        // 中等风险，可能需要进一步查看
        Medium = 2,

        // 高风险，通常需要拦截或处理
        High = 3,

        // 严重风险，用于最高等级的安全事件
        Critical = 4
    }
}
