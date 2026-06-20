namespace AIShield.Backend.Dtos
{
    public class OverviewResponse
    {
        // 总览页展示的指标数据，帮助用户快速了解Agent的整体运行情况和安全态势

        // 今日请求总数
        public int DayRequestCount { get; set; } = 0;

        // 今日拦截次数
        public int DayBlockedCount { get; set; } = 0;

        // 今日输出脱敏次数
        public int DayMaskedCount { get; set; } = 0;

        // 今日风险事件数（被拦截或脱敏的事件总数）
        public int DayRiskEventCount { get; set; } = 0;
    }
}
