using System.Collections;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice48PlayTests
    {
        private GameObject _root;
        private NulProductionController _controller;
        private ProductionGameLoop _loop;
        private ProductionShellFlow _shell;
        private ProductionHud _hud;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("slice48-shell");
            _root.SetActive(false);
            _root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var stand = new GameObject("stand").AddComponent<BoxCollider2D>();
            stand.transform.SetParent(_root.transform, false);
            var crouch = new GameObject("crouch").AddComponent<BoxCollider2D>();
            crouch.transform.SetParent(_root.transform, false);
            crouch.enabled = false;
            _controller = _root.AddComponent<NulProductionController>();
            _controller.udpPort = 8928;
            _controller.autoRun = false;
            SetPrivate(_controller, "standingCollider", stand);
            SetPrivate(_controller, "crouchCollider", crouch);
            _root.AddComponent<ProductionCourseLoop>();
            _root.AddComponent<ProductionObstacleJudge>();
            _loop = _root.AddComponent<ProductionGameLoop>();
            _shell = _root.AddComponent<ProductionShellFlow>();
            _hud = _root.AddComponent<ProductionHud>();
            _hud.GameLoop = _loop;
            _hud.ShellFlow = _shell;
            _root.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        [UnityTest]
        public IEnumerator StableHandRunsExactEntryAndRestartReturnsToTitle()
        {
            yield return null;
            Assert.AreEqual(ProductionEntryStage.Title, _shell.EntryStage);
            Assert.IsTrue(_controller.Nul.Paused);
            _controller.InjectDatagram(Landmarks());
            _shell.TickEntry(ProductionShellFlow.HandStableSeconds);
            Assert.AreEqual(ProductionEntryStage.ConnectionDetected,
                _shell.EntryStage);
            _shell.TickEntry(ProductionShellFlow.ConnectionDetectedSeconds);
            Assert.AreEqual(ProductionEntryStage.LinkEstablished,
                _shell.EntryStage);
            _shell.TickEntry(ProductionShellFlow.LinkEstablishedSeconds);
            Assert.AreEqual(ProductionEntryStage.Opening, _shell.EntryStage);
            _shell.TickEntry(ProductionShellFlow.OpeningSeconds);
            Assert.AreEqual(ProductionEntryStage.Running, _shell.EntryStage);
            Assert.IsFalse(_controller.Nul.Paused);

            _loop.RestartGame();
            Assert.AreEqual(ProductionEntryStage.Title, _shell.EntryStage);
            Assert.IsTrue(_controller.Nul.Paused);
        }

        [UnityTest]
        public IEnumerator ExitConfirmationDiscardsWithoutReplayAndCancelRestores()
        {
            yield return null;
            _controller.InjectDatagram(Landmarks());
            _shell.TickEntry(ProductionShellFlow.HandStableSeconds);
            _shell.TickEntry(ProductionShellFlow.ConnectionDetectedSeconds);
            _shell.TickEntry(ProductionShellFlow.LinkEstablishedSeconds);
            _shell.TickEntry(ProductionShellFlow.OpeningSeconds);

            _shell.RequestExitConfirmation();
            Assert.IsTrue(_shell.ExitConfirmationVisible);
            Assert.IsTrue(_controller.Nul.Paused);
            Assert.IsFalse(_hud.ShowPaused,
                "exit confirmation must replace, not overlap, Pause copy");
            _controller.InjectDatagram(Command(1, "STOP_ON"));
            Assert.IsFalse(_controller.Nul.StopActive);
            _shell.CancelExitConfirmation();
            Assert.IsFalse(_shell.ExitConfirmationVisible);
            Assert.IsFalse(_controller.Nul.Paused);
            _controller.InjectDatagram(Command(1, "STOP_ON"));
            Assert.IsFalse(_controller.Nul.StopActive,
                "deduped command received behind confirmation must not replay");
            _controller.InjectDatagram(Command(2, "STOP_ON"));
            Assert.IsTrue(_controller.Nul.StopActive);
        }

        private static string Landmarks()
        {
            var json = new StringBuilder("{\"kind\":\"lm\",\"pts\":[");
            for (int i = 0; i < 42; i++)
            {
                if (i > 0) json.Append(',');
                json.Append("0.5");
            }
            return json.Append("]}").ToString();
        }

        private static string Command(int id, string type)
            => "{\"v\":1,\"kind\":\"cmd\",\"session\":\"shell\",\"sstart\":1," +
               "\"cmdId\":" + id + ",\"seq\":" + id + ",\"tUs\":1," +
               "\"type\":\"" + type + "\",\"phase\":\"fire\"," +
               "\"conf\":1,\"poseDist\":0,\"motion\":1,\"quality\":1}";

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(info, field);
            info.SetValue(target, value);
        }
    }
}
