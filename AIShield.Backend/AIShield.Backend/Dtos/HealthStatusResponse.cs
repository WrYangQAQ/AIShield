namespace AIShield.Backend.Dtos
{
    public class HealthStatusResponse
    {
        // 健康分（保留整数使用，向下取整，范围0~100）
        public double HealthScore { get; set; } = 0;

        // 平均响应时间（保留整数使用，向上取整，单位为毫秒）
        public double AverageResponseTime { get; set; } = 0;

        // 错误率小数值，前端展示时乘以 100 转为百分比
        public double ErrorRate { get; set; } = 0;

        // 可用性小数值，前端展示时乘以 100 转为百分比
        public double Availability { get; set; } = 0;
    }
}
