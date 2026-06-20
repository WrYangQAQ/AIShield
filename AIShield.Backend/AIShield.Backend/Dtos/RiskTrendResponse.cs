namespace AIShield.Backend.Dtos
{
    public class RiskTrendResponse
    {
        // 查询的天数范围
        public int Days { get; set; }

        // 每天的风险趋势点
        public List<RiskTrendPointResponse> Points { get; set; } = new();
    }
}
