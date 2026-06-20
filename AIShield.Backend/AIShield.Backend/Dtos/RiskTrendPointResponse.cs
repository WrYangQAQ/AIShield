namespace AIShield.Backend.Dtos
{
    public class RiskTrendPointResponse
    {
        // 统计日期
        public DateOnly Date { get; set; }

        // 前端图表展示用日期标签
        public string Label { get; set; } = string.Empty;

        // 当日拦截次数
        public int BlockCount { get; set; }

        // 当日脱敏次数
        public int MaskCount { get; set; }

        // 当日高风险事件次数
        public int HighRiskCount { get; set; }
    }
}
