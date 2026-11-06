using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Shirabe
{
    /// <summary>調べる 1 回の結果。</summary>
    public sealed class LookupOutcome
    {
        public string Question = string.Empty;
        /// <summary>証拠つきの答えが出たか。</summary>
        public bool Found;
        public string Answer = string.Empty;
        /// <summary>ページに実在した根拠の一文。</summary>
        public string Quote = string.Empty;
        public string SourceTitle = string.Empty;
        public string SourceUrl = string.Empty;
        /// <summary>モデルが「足りない」と言った物 (見つからなかったとき)。</summary>
        public string Missing = string.Empty;
        public int PagesRead;
        public double ElapsedMs;
        /// <summary>evidence / not-found / rejected (引用がページに無い) / no-pages / budget / failed / cancelled。</summary>
        public string Reason = string.Empty;
        public List<string> Trace = new List<string>();
    }

    /// <summary>
    /// 範囲を切った調べる。検索 → 上位のページを読む → モデルに資料だけから答えさせる → 証拠の門 → 通れば答え、通らなければ「無かった」。
    /// 範囲 = ページ MaxPages 枚まで、Budget 秒で打ち切り、1 周だけ (問い直しはしない)。
    /// 検索・読む・聞く は口を渡す (純粋なので、偽の口でテストできる)。
    /// </summary>
    public sealed class BoundedLookup
    {
        public delegate Task<IReadOnlyList<SearchHit>> SearchFunc(string query, int maxResults, CancellationToken cancellationToken);
        public delegate Task<string> ReadFunc(string url, int maxChars, CancellationToken cancellationToken);
        public delegate Task<string> AskFunc(string prompt, CancellationToken cancellationToken);

        private readonly SearchFunc _search;
        private readonly ReadFunc _read;
        private readonly AskFunc _ask;

        public BoundedLookup(SearchFunc search, ReadFunc read, AskFunc ask)
        {
            _search = search ?? throw new ArgumentNullException(nameof(search));
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _ask = ask ?? throw new ArgumentNullException(nameof(ask));
        }

        public int MaxPages { get; set; } = 3;
        public TimeSpan Budget { get; set; } = TimeSpan.FromSeconds(45);
        public TimeSpan PageReadTimeout { get; set; } = TimeSpan.FromSeconds(6);
        /// <summary>1 枚から台本に載せる字数。</summary>
        public int PageChars { get; set; } = LookupPrompt.DefaultPageChars;
        /// <summary>読む口に頼む字数 (本文の頭からこれだけ)。</summary>
        public int ReadChars { get; set; } = 6000;

        public async Task<LookupOutcome> RunAsync(string question, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            var outcome = new LookupOutcome { Question = question ?? string.Empty };
            try
            {
                IReadOnlyList<SearchHit> results;
                try { results = await _search(question, MaxPages + 2, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { outcome.Trace.Add("検索に失敗: " + ex.Message); outcome.Reason = "failed"; return Finish(outcome, sw); }

                var pages = await ReadPagesAsync(results, sw, cancellationToken).ConfigureAwait(false);
                outcome.PagesRead = pages.Count;
                if (pages.Count == 0)
                {
                    outcome.Trace.Add("読めるページが無い (" + (results?.Count ?? 0) + " 件)");
                    outcome.Reason = "no-pages";
                    return Finish(outcome, sw);
                }

                var remaining = Budget - sw.Elapsed;
                if (remaining < TimeSpan.FromSeconds(3)) { outcome.Reason = "budget"; return Finish(outcome, sw); }
                string text;
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(remaining);
                    text = await _ask(LookupPrompt.Build(question, pages, PageChars), cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { outcome.Reason = "budget"; outcome.Trace.Add("読みが予算を超えた"); return Finish(outcome, sw); }
                catch (Exception ex) { outcome.Reason = "failed"; outcome.Trace.Add("読みに失敗: " + ex.Message); return Finish(outcome, sw); }

                var answer = LookupAnswer.Parse(text);
                outcome.Missing = answer.Missing;
                if (answer.NotFound || answer.Quote.Length == 0)
                {
                    outcome.Reason = "not-found";
                    outcome.Trace.Add("見つからない" + (answer.Missing.Length > 0 ? " (足りない: " + answer.Missing + ")" : string.Empty));
                    return Finish(outcome, sw);
                }

                var source = FindSource(answer, pages);
                if (source == null)
                {
                    outcome.Reason = "rejected";
                    outcome.Trace.Add("引用がページに無い → 捨てた: " + Short(answer.Quote));
                    return Finish(outcome, sw);
                }
                if (!AnswerNumbersInPages(answer.Answer, pages))
                {
                    // 引用で確かめたのは一文だけ。答えに別の数字 (別の店の時間など) を足してくることがあるので、答えの数字も全部ページに無ければ捨てる
                    outcome.Reason = "rejected";
                    outcome.Trace.Add("答えの数字がページに無い → 捨てた: " + Short(answer.Answer));
                    return Finish(outcome, sw);
                }

                outcome.Found = true;
                outcome.Answer = answer.Answer;
                outcome.Quote = answer.Quote;
                outcome.SourceTitle = source.Title;
                outcome.SourceUrl = source.Url;
                outcome.Reason = "evidence";
                outcome.Trace.Add("証拠あり");
                return Finish(outcome, sw);
            }
            catch (OperationCanceledException) { outcome.Reason = "cancelled"; return Finish(outcome, sw); }
        }

        private static LookupOutcome Finish(LookupOutcome o, Stopwatch sw) { o.ElapsedMs = sw.Elapsed.TotalMilliseconds; return o; }

        private async Task<List<LookupPage>> ReadPagesAsync(IReadOnlyList<SearchHit> results, Stopwatch sw, CancellationToken cancellationToken)
        {
            var pages = new List<LookupPage>();
            if (results == null) return pages;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tasks = new List<Task<LookupPage>>();
            foreach (var r in results)
            {
                if (tasks.Count >= MaxPages) break;
                if (r == null || !IsReadable(r.Url) || !seen.Add(r.Url)) continue;
                tasks.Add(ReadOneAsync(r, sw, cancellationToken));
            }
            foreach (var t in tasks)
            {
                var page = await t.ConfigureAwait(false);
                if (page != null) pages.Add(page);
            }
            return pages;
        }

        private async Task<LookupPage> ReadOneAsync(SearchHit r, Stopwatch sw, CancellationToken cancellationToken)
        {
            var remaining = Budget - sw.Elapsed;
            var timeout = PageReadTimeout < remaining ? PageReadTimeout : remaining;
            if (timeout <= TimeSpan.Zero) return null;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);
                var text = await _read(r.Url, ReadChars, cts.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text) || text.Length < 80) return null;   // 空か、本文の無いページ
                return new LookupPage(r.Title, r.Url, text);
            }
            catch (Exception) { return null; }   // 1 枚ぶんを諦める (読めないページは多い)
        }

        /// <summary>http(s) の普通のページだけ。PDF や動画・画像は読まない。</summary>
        public static bool IsReadable(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            var path = url.Split('?')[0].ToLowerInvariant();
            foreach (var ext in new[] { ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".zip", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx" })
                if (path.EndsWith(ext, StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>引用が実在するページ。番号の資料を先に見て、無ければ全部を見る。</summary>
        public static LookupPage FindSource(LookupAnswer answer, IReadOnlyList<LookupPage> pages)
        {
            if (answer.SourceIndex >= 1 && answer.SourceIndex <= pages.Count && EvidenceGate.Supports(answer.Quote, pages[answer.SourceIndex - 1].Text))
                return pages[answer.SourceIndex - 1];
            foreach (var p in pages)
                if (EvidenceGate.Supports(answer.Quote, p.Text)) return p;
            return null;
        }

        /// <summary>答えの中の数字の連が全部、読んだどれかのページにあるか。</summary>
        public static bool AnswerNumbersInPages(string answer, IReadOnlyList<LookupPage> pages)
        {
            var sb = new StringBuilder();
            foreach (var p in pages) sb.Append(p.Text).Append('|');
            return EvidenceGate.DigitsAllPresent(answer, sb.ToString());
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) || s.Length <= 40 ? s : s.Substring(0, 40) + "…";
    }
}
