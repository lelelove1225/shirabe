using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Shirabe
{
    /// <summary>
    /// 使い方: shirabe "問い" [--searxng URL] [--llm URL] [--model 名] [--pages 3] [--budget 45] [--llm-extra '{"id_slot":1}'] [--json]
    /// 環境変数 SHIRABE_SEARXNG / SHIRABE_LLM / SHIRABE_MODEL / SHIRABE_API_KEY / SHIRABE_LLM_EXTRA でも渡せる。
    /// 終了コード: 0 = 証拠つきの答え、1 = 見つからなかった (正直な無し)、2 = 使い方の誤り・失敗。
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // Shift_JIS / EUC-JP のページを読むため
            Console.OutputEncoding = Encoding.UTF8;

            string question = null;
            var searxng = Environment.GetEnvironmentVariable("SHIRABE_SEARXNG") ?? "http://localhost:8080";
            var llm = Environment.GetEnvironmentVariable("SHIRABE_LLM") ?? "http://localhost:8090";
            var model = Environment.GetEnvironmentVariable("SHIRABE_MODEL") ?? "default";
            var apiKey = Environment.GetEnvironmentVariable("SHIRABE_API_KEY");
            var llmExtra = Environment.GetEnvironmentVariable("SHIRABE_LLM_EXTRA");
            var pages = 3; var budget = 45; var json = false;
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : null;
                switch (a)
                {
                    case "--searxng": searxng = Next(); break;
                    case "--llm": llm = Next(); break;
                    case "--model": model = Next(); break;
                    case "--llm-extra": llmExtra = Next(); break;
                    case "--pages": pages = int.Parse(Next() ?? "3"); break;
                    case "--budget": budget = int.Parse(Next() ?? "45"); break;
                    case "--json": json = true; break;
                    case "-h": case "--help": return Usage();
                    default:
                        if (a.StartsWith("--", StringComparison.Ordinal)) return Usage("知らない引数: " + a);
                        question = question == null ? a : question + " " + a;
                        break;
                }
            }
            if (string.IsNullOrWhiteSpace(question)) return Usage("問いが無い");

            using var search = new SearxngSearch(searxng);
            using var reader = new HttpPageReader();
            using var chat = new OpenAiChat(llm, model, apiKey, timeoutSeconds: budget + 5) { ExtraJson = llmExtra };
            var lookup = new BoundedLookup(search.SearchAsync, reader.ReadAsync, chat.AskAsync) { MaxPages = pages, Budget = TimeSpan.FromSeconds(budget) };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            var outcome = await lookup.RunAsync(question, cts.Token).ConfigureAwait(false);

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(outcome, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, IncludeFields = true }));
                return outcome.Found ? 0 : outcome.Reason == "failed" ? 2 : 1;
            }
            Console.WriteLine(Render(outcome));
            return outcome.Found ? 0 : outcome.Reason == "failed" ? 2 : 1;
        }

        /// <summary>人が読む形。証拠つきなら 答え・根拠・出どころ、無ければ 読んだ枚数と足りない物。</summary>
        public static string Render(LookupOutcome o)
        {
            var sb = new StringBuilder();
            if (o.Found)
            {
                sb.Append("答え: ").Append(o.Answer).Append('\n');
                sb.Append("根拠: 「").Append(o.Quote).Append("」\n");
                sb.Append("出どころ: ").Append(o.SourceTitle).Append(" ").Append(o.SourceUrl).Append('\n');
                sb.Append("(これ以外のことは分かっていない)");
            }
            else
            {
                sb.Append(o.PagesRead).Append(" 枚読んだが、証拠つきの答えは無かった");
                if (o.Reason == "rejected") sb.Append(" (モデルの答えは出たが、引用がページに無かったので捨てた)");
                if (o.Reason == "no-pages") sb.Append(" (読めるページが無かった)");
                if (o.Reason == "budget") sb.Append(" (時間切れ)");
                if (o.Reason == "failed") sb.Append(" (失敗)");
                if (!string.IsNullOrEmpty(o.Missing) && o.Missing != "なし") sb.Append("\n足りない: ").Append(o.Missing);
            }
            sb.Append('\n').Append((o.ElapsedMs / 1000.0).ToString("0.0")).Append(" 秒");
            foreach (var t in o.Trace) sb.Append("\n  ").Append(t);
            return sb.ToString();
        }

        private static int Usage(string error = null)
        {
            if (error != null) Console.Error.WriteLine(error);
            Console.Error.WriteLine("使い方: shirabe \"問い\" [--searxng URL] [--llm URL] [--model 名] [--pages 3] [--budget 45] [--llm-extra JSON] [--json]");
            return 2;
        }
    }
}
