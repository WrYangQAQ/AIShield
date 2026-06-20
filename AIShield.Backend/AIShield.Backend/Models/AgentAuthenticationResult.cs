namespace AIShield.Backend.Models
{
    public class AgentAuthenticationResult
    {
        // 鉴权是否通过
        public bool Succeeded { get; set; }

        // 鉴权通过后识别出的 Agent
        public AgentApp? Agent { get; set; }

        // 鉴权失败时返回给调用方的原因
        public string Message { get; set; } = string.Empty;
    }
}
