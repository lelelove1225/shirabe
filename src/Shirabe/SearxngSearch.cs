using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shirabe
{
    /// <summary>
    /// SearXNG の JSON API を叩く: GET {base}/search?q=…&format=json&language=ja-JP。
    /// SearXNG 側の settings.yml で search.formats に json を足しておくこと (既定は html だけ)。
    /// </summary>
    public sealed class SearxngSearch : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string _language;

        public SearxngSearch(string baseUrl, string language = "ja-JP", float timeoutSeconds = 8f)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _language = language ?? "ja-JP";
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        }

        public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken)
        {
            var url = _baseUrl + "/search?q=" + Uri.EscapeDataString(query ?? string.Empty) + "&format=json&language=" + Uri.EscapeDataString(_language) + "&safesearch=1";
            using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("SearXNG " + (int)response.StatusCode + ": " + Truncate(body, 120));
            return Parse(body, maxResults);
        }

        /// <summary>results[].title / url / content を読む。題も本文も空の行は捨てる。</summary>
        public static IReadOnlyList<SearchHit> Parse(string json, int maxResults)
        {
            var list = new List<SearchHit>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) return list;
            foreach (var r in results.EnumerateArray())
            {
                var title = Str(r, "title");
                var content = Str(r, "content");
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content)) continue;
                list.Add(new SearchHit(title, content, Str(r, "url")));
                if (list.Count >= Math.Max(1, maxResults)) break;
            }
            return list;
        }

        private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : string.Empty;

        private static string Truncate(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n);

        public void Dispose() => _http.Dispose();
    }
}
