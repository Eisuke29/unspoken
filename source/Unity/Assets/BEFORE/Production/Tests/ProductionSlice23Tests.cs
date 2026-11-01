using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Before.Production.Tests
{
    public class ProductionSlice23Tests
    {
        [Test]
        public void ProductionRuntimeKeepsSingleOwnersForExternalBoundaries()
        {
            string runtime = Path.Combine(Application.dataPath, "BEFORE", "Production", "Runtime");
            string[] files = Directory.GetFiles(runtime, "*.cs", SearchOption.TopDirectoryOnly);
            var networkOwners = new List<string>();
            var processOwners = new List<string>();
            var preferenceOwners = new List<string>();
            string[] denied =
            {
                "Unity.Sentis", "MLAgents", "System.Net.Http",
                "WebCamTexture", "HttpClient", "TcpClient", "OpenAI", "Anthropic"
            };

            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string source = File.ReadAllText(file);
                foreach (string token in denied)
                    StringAssert.DoesNotContain(token, source, name + " contains " + token);
                if (source.Contains("System.Net")) networkOwners.Add(name);
                if (source.Contains("System.Diagnostics") || source.Contains("Process.Start"))
                    processOwners.Add(name);
                if (source.Contains("PlayerPrefs")) preferenceOwners.Add(name);
            }

            CollectionAssert.AreEquivalent(new[] { "UdpCommandReceiver.cs" }, networkOwners);
            CollectionAssert.AreEquivalent(new[] { "SidecarProcessManager.cs" }, processOwners);
            CollectionAssert.AreEquivalent(new[] { "ProductionAudioFeedback.cs" }, preferenceOwners);

            string udp = File.ReadAllText(Path.Combine(runtime, "UdpCommandReceiver.cs"));
            StringAssert.Contains("UdpClient", udp);
            StringAssert.DoesNotContain("TcpClient", udp);
            StringAssert.DoesNotContain("HttpClient", udp);

            string sidecar = File.ReadAllText(Path.Combine(runtime, "SidecarProcessManager.cs"));
            StringAssert.Contains("Path.Combine(projectRoot, \"Tools\", \"BeforeGestureSidecar\"",
                sidecar);
            string control = File.ReadAllText(Path.Combine(runtime,
                "SidecarTeachingControlClient.cs"));
            StringAssert.Contains("UnityEngine.Networking", control);
            StringAssert.Contains("http://127.0.0.1:", control);
            StringAssert.DoesNotContain("Keyboard", control);
            string audio = File.ReadAllText(Path.Combine(runtime, "ProductionAudioFeedback.cs"));
            StringAssert.Contains("MutePreferenceKey = \"BeforeMute\"", audio);
        }

        [Test]
        public void ProductionSceneDoesNotReferenceHeldBackgroundStaging()
        {
            string scene = File.ReadAllText(Path.Combine(Application.dataPath,
                "BEFORE", "Scenes", "BEFORE_ProductionV2.unity"));
            foreach (string heldReference in new[]
                     { "bg_r1_connection_outskirts", "concept_src", ".claude", "staging" })
                StringAssert.DoesNotContain(heldReference, scene);
        }
    }
}
