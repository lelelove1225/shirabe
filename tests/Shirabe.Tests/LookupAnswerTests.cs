using NUnit.Framework;
using Shirabe;

namespace Shirabe.Tests
{
    public sealed class LookupAnswerTests
    {
        [Test]
        public void Parse_ReadsTheFourLines()
        {
            var a = LookupAnswer.Parse("答え: 22 時まで\n引用: 「営業時間は 11:00〜22:00 です。」\n出どころ: 2\n足りない: なし\n");
            Assert.That(a.Answer, Is.EqualTo("22 時まで"));
            Assert.That(a.Quote, Is.EqualTo("営業時間は 11:00〜22:00 です。"));
            Assert.That(a.SourceIndex, Is.EqualTo(2));
            Assert.That(a.Missing, Is.EqualTo("なし"));
            Assert.That(a.NotFound, Is.False);
        }

        [Test]
        public void Parse_TheFirstLineMayComeWithoutItsName_BecauseThePromptEndsWithTheName()
        {
            var a = LookupAnswer.Parse(" 22 時まで\n引用: 営業時間は 11:00〜22:00 です。\n出どころ: １");
            Assert.That(a.Answer, Is.EqualTo("22 時まで"));
            Assert.That(a.SourceIndex, Is.EqualTo(1));
        }

        [Test]
        public void Parse_NotFound_CarriesWhatIsMissing()
        {
            var a = LookupAnswer.Parse("答え: 見つからない\n引用: \n出どころ: \n足りない: 出発地と目的地");
            Assert.That(a.NotFound, Is.True);
            Assert.That(a.Missing, Is.EqualTo("出発地と目的地"));
        }

        [Test]
        public void Parse_ToleratesBulletsAndFullWidthColons()
        {
            var a = LookupAnswer.Parse("- 答え： 火曜休み\n- 引用： 定休日は火曜日。\n- 出どころ： 1");
            Assert.That(a.Answer, Is.EqualTo("火曜休み"));
            Assert.That(a.Quote, Is.EqualTo("定休日は火曜日。"));
        }

        [Test]
        public void Parse_EmptyOrGarbage_IsNotFound()
        {
            Assert.That(LookupAnswer.Parse(string.Empty).NotFound, Is.True);
            Assert.That(LookupAnswer.Parse("わからないです").NotFound, Is.True);
        }

        [Test]
        public void Prompt_PutsEveryPageUnderItsNumber_AndEndsWithTheAnswerName()
        {
            var pages = new[] { new LookupPage("店 A", "https://a", "本文 A"), new LookupPage("店 B", "https://b", "本文 B") };
            var p = LookupPrompt.Build("何時まで？", pages);
            Assert.That(p, Does.Contain("【資料 1】店 A\n本文 A"));
            Assert.That(p, Does.Contain("【資料 2】店 B\n本文 B"));
            Assert.That(p, Does.EndWith("答え:"));
        }
    }
}
