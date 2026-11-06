using System.Text;

namespace Shirabe
{
    /// <summary>
    /// 証拠の門。モデルが答えに添えた「引用の一文」が、読んだページの本文に本当にあるかを機構で照合する。
    /// 小さいモデルは「資料に無いことは書かない」と頼んでも書くので、頼むのではなく、根拠の無い答えを捨てる。
    /// 間違った答えを言うより「見つからなかった」と言う方を選ぶ。
    ///
    /// この版の照合は厳しい: 空白を落としたうえで、引用が本文に丸ごと含まれているときだけ通す。
    /// 言い換えや記号の揺れは全部落ちるので、小さいモデルだと「無かった」が多くなる。それでも嘘は通らない。
    /// </summary>
    public static class EvidenceGate
    {
        /// <summary>引用はこれより短いと証拠にならない (「営業時間」だけでは何にでも当たる)。</summary>
        public const int MinQuoteChars = 8;

        /// <summary>比べる前の整え: 空白だけを落とす。文字そのものは変えない。</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || ch == '　') continue;
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>引用がページの本文に丸ごとあるか。</summary>
        public static bool Supports(string quote, string pageText)
        {
            var q = Normalize(quote);
            var p = Normalize(pageText);
            if (q.Length < MinQuoteChars || p.Length == 0) return false;
            return p.Contains(q);
        }

        /// <summary>
        /// 文の中の数字の連 (時刻・値段・日付) が全部、本文のどこかにあるか。
        /// 引用で確かめられるのは一文だけなので、答えの数字は別に見る。数が違う答えは答えでない。
        /// </summary>
        public static bool DigitsAllPresent(string text, string pageText)
        {
            var q = Normalize(text);
            var p = Normalize(pageText);
            var i = 0;
            while (i < q.Length)
            {
                if (!char.IsDigit(q[i])) { i++; continue; }
                var j = i;
                while (j < q.Length && char.IsDigit(q[j])) j++;
                if (!p.Contains(q.Substring(i, j - i))) return false;
                i = j;
            }
            return true;
        }
    }
}
