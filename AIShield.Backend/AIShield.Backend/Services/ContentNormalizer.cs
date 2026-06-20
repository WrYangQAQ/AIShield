using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AIShield.Backend.Services
{
    public class ContentNormalizer
    {
        private static readonly Regex MultiWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex AllWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        // 生成原文、规范化文本和常见编码解码文本，供规则引擎做多轮匹配
        public List<string> BuildDetectionVariants(string content)
        {
            var variants = new List<string>();

            AddVariant(variants, content);
            AddVariant(variants, NormalizeText(content));
            AddVariant(variants, WebUtility.UrlDecode(content));
            AddVariant(variants, WebUtility.HtmlDecode(content));
            AddVariant(variants, TryDecodeBase64(content));

            return variants;
        }

        // 将大小写、全角字符和多余空白统一成更适合安全检测的形式
        public string NormalizeText(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            // FormKC 会把 Unicode 兼容字符归一化，例如全角数字/字母和组合字符。
            var normalizedContent = content.Normalize(NormalizationForm.FormKC);
            var builder = new StringBuilder(normalizedContent.Length);

            foreach (var ch in normalizedContent)
            {
                // 将全角空格转为普通空格，降低简单编码绕过的影响。
                if (ch == '\u3000')
                {
                    builder.Append(' ');
                }
                else if (ch >= '\uFF01' && ch <= '\uFF5E')
                {
                    // 显式处理全角 ASCII，包括数字、大小写字母和常见符号。
                    builder.Append((char)(ch - 0xFEE0));
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return MultiWhitespaceRegex
                .Replace(builder.ToString().ToLowerInvariant(), " ")
                .Trim();
        }

        // 尝试将内容按 Base64 解码，如果解码失败则返回空字符串，避免抛出异常。
        private static string TryDecodeBase64(string content)
        {
            var compact = AllWhitespaceRegex.Replace(content, string.Empty);

            if (compact.Length < 8 || compact.Length % 4 != 0)
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(compact);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static void AddVariant(List<string> variants, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!variants.Contains(value))
            {
                variants.Add(value);
            }
        }
    }
}
