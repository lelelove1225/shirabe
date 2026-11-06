using NUnit.Framework;
using Shirabe;

namespace Shirabe.Tests
{
    public sealed class SearxngSearchTests
    {
        [Test]
        public void Parse_ReadsTitleUrlContent_AndSkipsEmptyRows()
        {
            var json = "{\"query\":\"q\",\"results\":[{\"title\":\"A\",\"url\":\"https://a\",\"content\":\"本文 A\"},{\"title\":\"\",\"url\":\"https://x\",\"content\":\"\"},{\"title\":\"B\",\"url\":\"https://b\",\"content\":\"本文 B\"}]}";
            var hits = SearxngSearch.Parse(json, 10);
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits[0].Title, Is.EqualTo("A"));
            Assert.That(hits[0].Url, Is.EqualTo("https://a"));
            Assert.That(hits[1].Snippet, Is.EqualTo("本文 B"));
        }

        [Test]
        public void Parse_StopsAtMaxResults_AndSurvivesNoResults()
        {
            var json = "{\"results\":[{\"title\":\"A\",\"url\":\"u\",\"content\":\"c\"},{\"title\":\"B\",\"url\":\"u\",\"content\":\"c\"}]}";
            Assert.That(SearxngSearch.Parse(json, 1).Count, Is.EqualTo(1));
            Assert.That(SearxngSearch.Parse("{}", 5).Count, Is.EqualTo(0));
        }

        [Test]
        public void OpenAiChat_BuildBody_MergesExtraFields()
        {
            using var chat = new OpenAiChat("http://x", "m") { ExtraJson = "{\"id_slot\":5,\"max_tokens\":50}" };
            var body = chat.BuildBody("p");
            Assert.That(body, Does.Contain("\"id_slot\":5"));
            Assert.That(body, Does.Contain("\"max_tokens\":50"));
            Assert.That(body, Does.Not.Contain("\"max_tokens\":400"));
            Assert.That(body, Does.Contain("\"model\":\"m\""));
        }

        [Test]
        public void OpenAiChat_ExtractContent_ReadsTheFirstChoice()
        {
            var json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"答え: 22 時まで\"}}]}";
            Assert.That(OpenAiChat.ExtractContent(json), Is.EqualTo("答え: 22 時まで"));
            Assert.That(OpenAiChat.ExtractContent("{\"choices\":[]}"), Is.EqualTo(string.Empty));
        }
    }
}
