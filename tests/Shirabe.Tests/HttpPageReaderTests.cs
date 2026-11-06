using System.Text;
using NUnit.Framework;
using Shirabe;

namespace Shirabe.Tests
{
    public sealed class HttpPageReaderTests
    {
        [Test]
        public void ExtractText_DropsScriptsNavAndTags_KeepsLines()
        {
            var html = "<html><head><title>t</title><script>var x = 1;</script><style>p{}</style></head><body><nav>メニュー</nav><p>一行目です。</p><div>二行目 &amp; 三行目</div></body></html>";
            var text = HttpPageReader.ExtractText(html, 1000);
            Assert.That(text, Is.EqualTo("一行目です。\n二行目 & 三行目"));
        }

        [Test]
        public void ExtractText_PrefersMainWhenItIsLongEnough()
        {
            var body = new string('あ', 600);
            var html = "<body><div>広告 広告 広告</div><main><p>" + body + "</p></main><footer>著作権</footer></body>";
            var text = HttpPageReader.ExtractText(html, 10000);
            Assert.That(text, Is.EqualTo(body));
        }

        [Test]
        public void ExtractText_CutsAtMaxChars()
        {
            var html = "<p>" + new string('い', 500) + "</p>";
            Assert.That(HttpPageReader.ExtractText(html, 100).Length, Is.EqualTo(100));
        }

        [Test]
        public void Decode_UsesTheMetaCharset_WhenTheHeaderHasNone()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("shift_jis");
            var bytes = sjis.GetBytes("<html><head><meta charset=\"Shift_JIS\"></head><body>日本語</body></html>");
            Assert.That(HttpPageReader.Decode(bytes, null), Does.Contain("日本語"));
        }
    }
}
