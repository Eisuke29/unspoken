using Before.Presentation;
using NUnit.Framework;

namespace Before.Tests
{
    /// <summary>
    /// D-096: BeforeTextCatalog is the single source of public SYS text. These lock the exact confirmed
    /// wording and the "never show 3/3 together with もう一度" / "no motion instruction on Stop" constraints.
    /// </summary>
    public sealed class BeforeTextCatalogTests
    {
        [Test]
        public void JumpStopDirection_Announcements_MatchSpec()
        {
            Assert.AreEqual("SYS// NULはこの先へ進めません", BeforeTextCatalog.JUMP_ANNOUNCE_1);
            Assert.AreEqual("越えるための合図を教えてください", BeforeTextCatalog.JUMP_ANNOUNCE_2);
            Assert.AreEqual("SYS// 前方に周期障害を検出", BeforeTextCatalog.STOP_ANNOUNCE_1);
            Assert.AreEqual("NULに待つ合図を教えてください", BeforeTextCatalog.STOP_ANNOUNCE_2);
            Assert.AreEqual("SYS// NULには経路全体が見えません", BeforeTextCatalog.DIRECTION_ANNOUNCE_1);
            Assert.AreEqual("進む方向を指で示してください", BeforeTextCatalog.DIRECTION_ANNOUNCE_2);
        }

        [Test]
        public void StopTeach_HasNoMotionInstruction()
        {
            Assert.AreEqual("SYS// 待つ合図の形を記録", BeforeTextCatalog.STOP_TEACH_1);
            Assert.AreEqual("SPACEを押したまま形を保つ", BeforeTextCatalog.STOP_TEACH_2);
            // Stop is a static sign: its teaching text must not tell the player to MOVE the hand.
            Assert.IsFalse(BeforeTextCatalog.STOP_TEACH_2.Contains("動かす"), "Stop teach must not instruct motion");
        }

        [Test]
        public void RemainingCounts_AreDistinctFromConfirmed()
        {
            Assert.AreEqual("同じ組合せをあと2回", BeforeTextCatalog.REMAIN_TWO);
            Assert.AreEqual("同じ組合せをあと1回", BeforeTextCatalog.REMAIN_ONE);
            Assert.AreEqual("SYS// 対応を記憶しました", BeforeTextCatalog.LEARNED_1);
            // the confirmed line must never itself carry a "もう一度" retry prompt
            Assert.IsFalse(BeforeTextCatalog.LEARNED_1.Contains("もう一度"));
            // outlier text carries no completion count ("N/3")
            Assert.AreEqual("SYS// 合図が安定しません", BeforeTextCatalog.SAMPLE_CONFLICT_1);
            Assert.IsFalse(BeforeTextCatalog.SAMPLE_CONFLICT_1.Contains("/3"));
        }
    }
}
