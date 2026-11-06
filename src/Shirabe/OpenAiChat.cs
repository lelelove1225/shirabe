using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shirabe
{
    /// <summary>
    /// OpenAI 互換の chat/completions を 1 往復だけ叩く口。llama.cpp の llama-server、LM Studio、Ollama など、/v1/chat/completions を持つ物なら相手にできる。
    /// temperature 0。思考 (reasoning) は使わない。
    /// </summary>
    public sealed class OpenAiChat : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string _model;
        private readonly string _apiKey;

        public OpenAiChat(string baseUrl, string model, string apiKey = null, float timeoutSeconds = 60f)
        {
            _url = (baseUrl ?? string.Empty).TrimEnd('/') + "/v1/chat/completions";
            _model = string.IsNullOrWhiteSpace(model) ? "default" : model;
            _apiKey = apiKey;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        }

        public int MaxTokens { get; set; } = 400;

        /// <summary>要求の body に混ぜる追加のフィールド (JSON のオブジェクト)。llama-server の id_slot / cache_prompt など。null なら足さない。</summary>
        public string ExtraJson { get; set; }

        public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken)
        {
            var body = BuildBody(prompt);
            using var request = new HttpRequestMessage(HttpMethod.Post, _url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (!string.IsNullOrEmpty(_apiKey)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("LLM " + (int)response.StatusCode + ": " + (text.Length > 200 ? text.Substring(0, 200) : text));
            return ExtractContent(text);
        }

        /// <summary>要求の body。ExtraJson のフィールドは後から混ぜる (同じ名前なら ExtraJson が勝つ)。</summary>
        public string BuildBody(string prompt)
        {
            var fields = new System.Collections.Generic.Dictionary<string, object>
            {
                ["model"] = _model,
                ["messages"] = new[] { new { role = "user", content = prompt } },
                ["temperature"] = 0,
                ["max_tokens"] = MaxTokens,
                ["stream"] = false,
            };
            if (!string.IsNullOrWhiteSpace(ExtraJson))
            {
                using var extra = JsonDocument.Parse(ExtraJson);
                foreach (var prop in extra.RootElement.EnumerateObject()) fields[prop.Name] = prop.Value.Clone();
            }
            return JsonSerializer.Serialize(fields);
        }

        /// <summary>choices[0].message.content を読む。</summary>
        public static string ExtractContent(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return string.Empty;
            var message = choices[0].GetProperty("message");
            return message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : string.Empty;
        }

        public void Dispose() => _http.Dispose();
    }
}
