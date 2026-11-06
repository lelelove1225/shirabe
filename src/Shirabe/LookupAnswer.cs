using System;
using System.Collections.Generic;
using System.Text;

namespace Shirabe
{
    /// <summary>読んだページ 1 枚 (題・URL・本文)。</summary>
    public sealed class LookupPage
    {
        public LookupPage(string title, string url, string text)
        {
            Title = title ?? string.Empty;
            Url = url ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string Title { get; }
        public string Url { get; }
        public string Text { get; }
    }

    /// <summary>
    /// モデルの答えの形: 「答え: …」「引用: …」「出どころ: N」、無ければ「答え: 見つからない」「足りない: …」。
    /// 行の名前で読む (番号や記号の揺れは許す)。読めない形は NotFound。
    /// </summary>
    public sealed class LookupAnswer
    {
        public string Answer = string.Empty;
        public string Quote = string.Empty;
        /// <summary>引用したページの番号 (1 から)。0 = 書いていない。</summary>
        public int SourceIndex;
        public string Missing = string.Empty;

        public bool NotFound =>
            Answer.Length == 0
            || Answer.StartsWith("見つからない", StringComparison.Ordinal)
            || Answer.StartsWith("分からない", StringComparison.Ordinal)
            || Answer.StartsWith("わからない", StringComparison.Ordinal)
            || Answer.StartsWith("不明", StringComparison.Ordinal);

        public static LookupAnswer Parse(string text)
        {
            var a = new LookupAnswer();
            if (string.IsNullOrWhiteSpace(text)) return a;
            foreach (var raw in text.Replace("\r", string.Empty).Split('\n'))
            {
                var line = raw.Trim().TrimStart('-', '*', '・', ' ');
                if (TryField(line, "答え", out var v)) a.Answer = v;
                else if (TryField(line, "引用", out v)) a.Quote = v.Trim('「', '」', '"', '“', '”', '『', '』');
                else if (TryField(line, "出どころ", out v) || TryField(line, "出所", out v)) a.SourceIndex = LeadingNumber(v);
                else if (TryField(line, "足りない", out v)) a.Missing = v;
                else if (a.Answer.Length == 0 && line.Length > 0 && !line.Contains(":") && !line.Contains("："))
                    a.Answer = line;   // 台本が「答え:」で終わるので、最初の行は名前なしで来ることがある
            }
            return a;
        }

        private static bool TryField(string line, string name, out string value)
        {
            value = null;
            if (!line.StartsWith(name, StringComparison.Ordinal)) return false;
            var rest = line.Substring(name.Length).TrimStart();
            if (rest.Length == 0 || (rest[0] != ':' && rest[0] != '：')) return false;
            value = rest.Substring(1).Trim();
            return true;
        }

        private static int LeadingNumber(string s)
        {
            var n = 0; var seen = false;
            foreach (var ch in s)
            {
                var c = ch >= '０' && ch <= '９' ? (char)('0' + (ch - '０')) : ch;
                if (c >= '0' && c <= '9') { n = n * 10 + (c - '0'); seen = true; }
                else if (seen) break;
            }
            return seen ? n : 0;
        }
    }

    /// <summary>台本。資料だけから答えさせ、根拠の一文をそのまま写させる。守らなくても EvidenceGate が落とす (指示は効かない前提で、機構で縛る)。</summary>
    public static class LookupPrompt
    {
        public const int DefaultPageChars = 2500;

        public static string Build(string question, IReadOnlyList<LookupPage> pages, int pageChars = DefaultPageChars)
        {
            var sb = new StringBuilder();
            sb.Append("次の資料だけを根拠に、問いに短く答える。資料に書いてないことは書かない。資料に答えが無ければ「答え: 見つからない」と書き、「足りない:」に何が無いかを書く。\n");
            sb.Append("答えの根拠になる一文を、資料から一字も変えずにそのまま写して「引用:」に書く (自分の言葉で言い換えない)。「出どころ:」はその資料の番号。\n");
            sb.Append("形式 (この 4 行だけ、前後に説明を書かない):\n答え: 60 字以内\n引用: 資料の一文そのまま\n出どころ: 番号\n足りない: (答えが見つかったときは「なし」)\n\n");
            sb.Append("問い: ").Append(question ?? string.Empty).Append("\n\n");
            for (var i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                var text = p.Text.Length > pageChars ? p.Text.Substring(0, pageChars) + "…" : p.Text;
                sb.Append("【資料 ").Append(i + 1).Append("】").Append(p.Title).Append('\n').Append(text).Append("\n\n");
            }
            sb.Append("答え:");
            return sb.ToString();
        }
    }
}
