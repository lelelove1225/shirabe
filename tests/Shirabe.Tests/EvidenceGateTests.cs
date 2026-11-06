using NUnit.Framework;
using Shirabe;

namespace Shirabe.Tests
{
    public sealed class EvidenceGateTests
    {
        private const string Page = "営業時間は 11:00〜22:00 です。\n定休日は火曜日。\n電話 03-1234-5678";

        [Test]
        public void Supports_WhenTheQuoteIsInThePageVerbatim()
        {
            Assert.That(EvidenceGate.Supports("営業時間は 11:00〜22:00 です。", Page), Is.True);
        }

        [Test]
        public void Supports_IgnoresWhitespaceOnly()
        {
            // 空白の揺れだけは許す (モデルは写すときに空白を落としたり足したりする)
            Assert.That(EvidenceGate.Supports("営業時間は11:00〜22:00です。", Page), Is.True);
            Assert.That(EvidenceGate.Supports("営業時間は　11:00 〜 22:00 です。", Page), Is.True);
        }

        [Test]
        public void Rejects_AParaphrase_EvenWhenItIsAlmostTheSame()
        {
            // 「です」→「だ」、「〜」→「から」のような言い換えは 1 字でも落ちる。言い換えは証拠でない (句点を落としただけの短い写しは、本文の一部そのままなので通る)
            Assert.That(EvidenceGate.Supports("営業時間は 11:00〜22:00 だ。", Page), Is.False);
            Assert.That(EvidenceGate.Supports("営業時間は11:00から22:00です。", Page), Is.False);
        }

        [Test]
        public void Rejects_AQuoteThatIsTooShortToBeEvidence()
        {
            Assert.That(EvidenceGate.Supports("営業時間", Page), Is.False);
            Assert.That(EvidenceGate.Supports("定休日は火曜日。", Page), Is.True);
        }

        [Test]
        public void Rejects_WhenThePageIsEmpty()
        {
            Assert.That(EvidenceGate.Supports("営業時間は 11:00〜22:00 です。", string.Empty), Is.False);
            Assert.That(EvidenceGate.Supports(null, Page), Is.False);
        }

        [Test]
        public void DigitsAllPresent_NeedsEveryNumberRunInThePage()
        {
            Assert.That(EvidenceGate.DigitsAllPresent("22時まで、火曜休み、電話は 1234", Page), Is.True);
            Assert.That(EvidenceGate.DigitsAllPresent("24時まで", Page), Is.False);      // 24 は無い (23 は電話番号の 1234 の中にある = 数字の照合はこの程度に粗い)
            Assert.That(EvidenceGate.DigitsAllPresent("数字なし", Page), Is.True);
        }
    }
}
