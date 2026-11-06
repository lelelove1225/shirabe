using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Shirabe
{
    /// <summary>
    /// HTTP でページを取って本文の文字にする。script / style / nav / title を落とし、main / article があればそこだけ、タグを落として行に。
    /// 文字コードはヘッダか meta の charset (日本語の店のページは Shift_JIS が残っている。Program で CodePagesEncodingProvider を登録する)。
    /// 1.5 MB より大きい応答は頭だけ。本文の抜き出し (ExtractText) は静的でテストできる。
    /// </summary>
    public sealed class HttpPageReader : IDisposable
    {
        private readonly HttpClient _http;
        public const int MaxBytes = 1_500_000;

        public HttpPageReader(float timeoutSeconds = 8f)
        {
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 4 }) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; shirabe/1.0)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ja,en;q=0.5");
        }

        public async Task<string> ReadAsync(string url, int maxChars, CancellationToken cancellationToken)
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode);
            var type = response.Content.Headers.ContentType;
            if (type != null && type.MediaType != null && !type.MediaType.Contains("html") && !type.MediaType.Contains("xml") && !type.MediaType.StartsWith("text/", StringComparison.Ordinal))
                throw new InvalidOperationException("本文でない: " + type.MediaType);
            byte[] bytes;
            using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[16384];
                int n;
                while ((n = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buffer, 0, n);
                    if (ms.Length >= MaxBytes) break;
                }
                bytes = ms.ToArray();
            }
            var html = Decode(bytes, type?.CharSet);
            return ExtractText(html, maxChars);
        }

        private static readonly Regex MetaCharset = new Regex(@"<meta[^>]+charset\s*=\s*[""']?\s*([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>ヘッダの charset、無ければ meta の charset、どちらも無ければ UTF-8。</summary>
        public static string Decode(byte[] bytes, string headerCharset)
        {
            var name = headerCharset;
            if (string.IsNullOrWhiteSpace(name))
            {
                var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
                var m = MetaCharset.Match(head);
                if (m.Success) name = m.Groups[1].Value;
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                try { return Encoding.GetEncoding(name.Trim().Trim('"', '\'')).GetString(bytes); } catch (Exception) { }
            }
            return Encoding.UTF8.GetString(bytes);
        }

        private static readonly Regex Drop = new Regex(@"<(script|style|noscript|template|svg|iframe|nav|header|footer|aside|form|title)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Comments = new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Main = new Regex(@"<(main|article)\b[^>]*>(.*?)</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Breaks = new Regex(@"<\s*(br|/p|/div|/li|/h[1-6]|/tr|/td|/th|/dd|/dt|/section|/table|/blockquote)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Tags = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"[ \t 　]+", RegexOptions.Compiled);

        /// <summary>HTML → 本文の行。main / article があればその中だけ (500 字以上あるとき)。短すぎる行 (1 文字) は落とす。</summary>
        public static string ExtractText(string html, int maxChars)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var s = Comments.Replace(html, " ");
            s = Drop.Replace(s, " ");
            var main = Main.Match(s);
            if (main.Success && main.Groups[2].Value.Length >= 500) s = main.Groups[2].Value;
            s = Breaks.Replace(s, "\n");
            s = Tags.Replace(s, " ");
            s = WebUtility.HtmlDecode(s);
            var sb = new StringBuilder();
            foreach (var raw in s.Split('\n'))
            {
                var line = Spaces.Replace(raw, " ").Trim();
                if (line.Length < 2) continue;
                sb.Append(line).Append('\n');
                if (sb.Length >= maxChars) break;
            }
            var text = sb.ToString().TrimEnd();
            return text.Length > maxChars ? text.Substring(0, maxChars) : text;
        }

        public void Dispose() => _http.Dispose();
    }
}
