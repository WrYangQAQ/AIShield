using AIShield.Backend.Enums;

namespace AIShield.Backend.Helpers
{
    public static class MemorySourceParser
    {
        public static bool TryParse(string? value, out MemorySource source)
        {
            source = MemorySource.Unknown;

            if (string.IsNullOrEmpty(value)) 
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant()
                .Replace("-", "_")
                .Replace(".", "_")
                .Replace("/", "_")
                .Replace(" ", "_");

            source = normalized switch
            {
                "unknown" => MemorySource.Unknown,
                "user" or "customer" => MemorySource.User,
                "system" or "sys" => MemorySource.System,
                "admin" or "administrator" => MemorySource.Admin,
                "doc" or "docu" or "document" or "file" or "knowledge_base" => MemorySource.Document,
                "web" or "website" or "webpage" or "web_page" or "url" => MemorySource.Website,
                "chat" or "conversation" or "dialog" or "dialogue" => MemorySource.Conversation,
                "tool" or "tool_call" or "plugin" => MemorySource.Tool,
                "import" or "bulk_import" or "migration" => MemorySource.Import,
                _ => ParsePrefixedSource(normalized)
            };

            return source != MemorySource.Unknown || normalized == "unknown";
        }

        private static MemorySource ParsePrefixedSource(string value)
        {
            if (value.StartsWith("user_"))
            {
                return MemorySource.User;
            }

            if (value.StartsWith("system_"))
            {
                return MemorySource.System;
            }

            if (value.StartsWith("admin_"))
            {
                return MemorySource.Admin;
            }

            if (value.StartsWith("document_") || value.StartsWith("doc_"))
            {
                return MemorySource.Document;
            }

            if (value.StartsWith("website_") || value.StartsWith("web_"))
            {
                return MemorySource.Website;
            }

            return MemorySource.Unknown;
        }
    }
}
