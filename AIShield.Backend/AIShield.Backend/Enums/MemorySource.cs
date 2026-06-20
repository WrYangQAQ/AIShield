using System.Text.Json.Serialization;
using AIShield.Backend.Helpers;

namespace AIShield.Backend.Enums
{
    [JsonConverter(typeof(MemorySourceJsonConverter))]
    public enum MemorySource
    {
        // 来源未知，无法确认
        Unknown = 0,

        // 用户直接提供
        User = 1,

        // 系统预设或生成
        System = 2,

        // 管理员提供或配置
        Admin = 3,

        // 从文档或知识库中提取
        Document = 4,

        // 从网页中总结获取
        Website = 5,

        // 从多轮对话中提取
        Conversation = 6,

        // Agent相关Plugin调用后产生
        Tool = 7,

        // 从其他系统批量导入
        Import = 8
    }
}
