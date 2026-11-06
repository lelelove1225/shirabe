using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Shirabe;

namespace Shirabe.Tests
{
    /// <summary>検索・読む・聞く を偽の口にして、流れと門を確かめる。</summary>
    public sealed class BoundedLookupTests
    {
        private const string PageA = "店 A の案内。営業時間は 11:00〜22:00 です。定休日は火曜日。";
        private const string PageB = "店 B の案内。営業時間は 10:00〜21:00 です。";

        private static Task<IReadOnlyList<SearchHit>> Search(string q, int n, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SearchHit>>(new[] { new SearchHit("店 A", "", "https://a.example/"), new SearchHit("店 B", "", "https://b.example/") });

        private static Task<string> Read(string url, int max, CancellationToken ct) =>
            Task.FromResult(url.Contains("a.example") ? Pad(PageA) : Pad(PageB));

        // 読む口は 80 字未満を「本文なし」として捨てるので、埋める
        private static string Pad(string s) => s + "\n" + new string('。', 100);

        private static BoundedLookup Make(string reply, BoundedLookup.SearchFunc search = null, BoundedLookup.ReadFunc read = null) =>
            new BoundedLookup(search ?? Search, read ?? Read, (p, ct) => Task.FromResult(reply));

        [Test]
        public async Task Evidence_WhenTheQuoteIsInAPage_AndTheAnswerNumbersAreToo()
        {
            var o = await Make("答え: 22 時まで\n引用: 営業時間は 11:00〜22:00 です。\n出どころ: 1\n足りない: なし").RunAsync("店 A は何時まで？", CancellationToken.None);
            Assert.That(o.Found, Is.True);
            Assert.That(o.Reason, Is.EqualTo("evidence"));
            Assert.That(o.SourceUrl, Is.EqualTo("https://a.example/"));
            Assert.That(o.PagesRead, Is.EqualTo(2));
        }

        [Test]
        public async Task Evidence_FindsTheSource_EvenWhenTheModelNamedTheWrongNumber()
        {
            var o = await Make("答え: 22 時まで\n引用: 営業時間は 11:00〜22:00 です。\n出どころ: 2").RunAsync("q", CancellationToken.None);
            Assert.That(o.Found, Is.True);
            Assert.That(o.SourceTitle, Is.EqualTo("店 A"));
        }

        [Test]
        public async Task Rejected_WhenTheQuoteIsNotInAnyPage()
        {
            // 言い換えた引用 (「から」) は門で落ちる。モデルの答えがあっても届けない
            var o = await Make("答え: 22 時まで\n引用: 営業時間は 11:00 から 22:00 です。\n出どころ: 1").RunAsync("q", CancellationToken.None);
            Assert.That(o.Found, Is.False);
            Assert.That(o.Reason, Is.EqualTo("rejected"));
            Assert.That(o.Answer, Is.Empty);
        }

        [Test]
        public async Task Rejected_WhenTheAnswerHasANumberThatNoPageHas()
        {
            // 引用は本物でも、答えに別の数字 (23 時) を足していたら捨てる
            var o = await Make("答え: 23 時まで\n引用: 営業時間は 11:00〜22:00 です。\n出どころ: 1").RunAsync("q", CancellationToken.None);
            Assert.That(o.Found, Is.False);
            Assert.That(o.Reason, Is.EqualTo("rejected"));
            Assert.That(o.Trace[0], Does.Contain("答えの数字がページに無い"));
        }

        [Test]
        public async Task NotFound_CarriesWhatIsMissing()
        {
            var o = await Make("答え: 見つからない\n足りない: 出発地と目的地").RunAsync("q", CancellationToken.None);
            Assert.That(o.Found, Is.False);
            Assert.That(o.Reason, Is.EqualTo("not-found"));
            Assert.That(o.Missing, Is.EqualTo("出発地と目的地"));
        }

        [Test]
        public async Task NoPages_WhenEveryHitIsUnreadable()
        {
            BoundedLookup.SearchFunc pdfOnly = (q, n, ct) => Task.FromResult<IReadOnlyList<SearchHit>>(new[] { new SearchHit("x", "", "https://x.example/a.pdf"), new SearchHit("y", "", "ftp://y") });
            var o = await Make("答え: 何か", pdfOnly).RunAsync("q", CancellationToken.None);
            Assert.That(o.Reason, Is.EqualTo("no-pages"));
            Assert.That(o.PagesRead, Is.EqualTo(0));
        }

        [Test]
        public async Task Failed_WhenTheSearchThrows()
        {
            BoundedLookup.SearchFunc broken = (q, n, ct) => throw new InvalidOperationException("SearXNG 502");
            var o = await Make("答え: 何か", broken).RunAsync("q", CancellationToken.None);
            Assert.That(o.Reason, Is.EqualTo("failed"));
            Assert.That(o.Trace[0], Does.Contain("SearXNG 502"));
        }

        [Test]
        public async Task Budget_StopsAModelThatTakesTooLong()
        {
            var lookup = new BoundedLookup(Search, Read, async (p, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return "答え: 遅い"; })
            { Budget = TimeSpan.FromSeconds(3.5) };
            var o = await lookup.RunAsync("q", CancellationToken.None);
            Assert.That(o.Reason, Is.EqualTo("budget"));
            Assert.That(o.ElapsedMs, Is.LessThan(10000));
        }

        [Test]
        public void IsReadable_OnlyHttpPages()
        {
            Assert.That(BoundedLookup.IsReadable("https://a.example/page?x=1"), Is.True);
            Assert.That(BoundedLookup.IsReadable("https://a.example/file.PDF"), Is.False);
            Assert.That(BoundedLookup.IsReadable("mailto:a@b"), Is.False);
        }
    }
}
