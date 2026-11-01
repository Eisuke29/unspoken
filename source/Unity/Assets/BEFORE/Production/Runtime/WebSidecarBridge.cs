// Web版（WebGL）用の橋渡し。
// ページの JavaScript（手の認識）が、Python の Sidecar と同じ JSON を
// unityInstance.SendMessage("BeforeWebBridge", "Datagram", json) で送ってくる。
// 受け取った JSON は UDP で届いたものと同じ扱いで UdpCommandReceiver に渡す。
using UnityEngine;

namespace Before.Production
{
    public sealed class WebSidecarBridge : MonoBehaviour
    {
        public const string ObjectName = "BeforeWebBridge";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var go = new GameObject(ObjectName);
            DontDestroyOnLoad(go);
            go.AddComponent<WebSidecarBridge>();
#endif
        }

        // JavaScript から呼ばれる（1回に1つのデータグラム）
        public void Datagram(string json)
        {
            UdpCommandReceiver.EnqueueWeb(json);
        }
    }
}
