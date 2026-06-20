namespace AIShield.Backend.Enums
{
    public enum SecurityAction
    {
        // 允许请求或内容继续执行
        Allow,

        // 允许通过，但标记为需要关注
        Warn,

        // 拦截请求或内容
        Block,

        // 将命中的敏感内容替换为安全占位符
        Mask,

        // 继续执行前需要人工确认
        NeedApproval
    }
}
