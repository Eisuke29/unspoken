using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Before.Production.Tests
{
    public class ProductionSlice13PlayTests
    {
        [UnityTest]
        public IEnumerator DiagnosticsReadsOnlyTwentyOneNormalizedSkeletonPoints()
        {
            var root = new GameObject("slice13-diagnostics");
            root.SetActive(false);
            root.AddComponent<Rigidbody2D>().gravityScale = 0f;
            var controller = root.AddComponent<NulProductionController>();
            controller.udpPort = 8889;
            controller.autoRun = false;
            var diagnostics = root.AddComponent<ProductionDiagnosticsHud>();
            diagnostics.Controller = controller;
            root.SetActive(true);
            yield return null;

            var json = new StringBuilder("{\"kind\":\"lm\",\"pts\":[");
            for (int i = 0; i < 21; i++)
            {
                if (i > 0) json.Append(',');
                json.Append((i / 20f).ToString(System.Globalization.CultureInfo.InvariantCulture));
                json.Append(',');
                json.Append((1f - i / 20f).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            json.Append("]}");
            controller.InjectDatagram(json.ToString());

            Assert.IsTrue(diagnostics.HasSkeleton);
            Assert.IsTrue(controller.TryGetLandmark(0, out Vector2 first));
            Assert.IsTrue(controller.TryGetLandmark(20, out Vector2 last));
            Assert.AreEqual(new Vector2(0f, 1f), first);
            Assert.AreEqual(new Vector2(1f, 0f), last);
            Assert.IsFalse(controller.TryGetLandmark(21, out _));

            Object.Destroy(root);
        }
    }
}
