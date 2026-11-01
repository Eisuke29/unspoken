// BEFORE Goal 2 — local UDP receiver for the sidecar link. 127.0.0.1 only.
// Receives command + skeleton datagrams on a background thread and hands them
// to the main thread via Poll(). Never receives or stores any RGB.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Before.Production
{
    public sealed class UdpCommandReceiver : MonoBehaviour
    {
        public int port = 8765;

        private UdpClient _client;
        private Thread _thread;
        private volatile bool _running;
        private readonly object _lock = new object();
        private readonly Queue<string> _queue = new Queue<string>();
        public int TotalReceived { get; private set; }

        public bool Listening => _running;

        // Web版：JavaScript の認識から届いた JSON（WebSidecarBridge 経由）
        private static readonly object s_webLock = new object();
        private static readonly Queue<string> s_webQueue = new Queue<string>();

        public static void EnqueueWeb(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            lock (s_webLock) { s_webQueue.Enqueue(json); }
        }

        public void StartListening()
        {
            if (_running) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // ブラウザでは UDP が使えないので、ソケットは開かずに Web の受け口だけ使う
            _running = true;
            return;
#endif
            try
            {
                // ReuseAddress avoids "address in use" after a domain reload left a
                // lingering socket; bind Any so 127.0.0.1 datagrams arrive.
                _client = new UdpClient();
                _client.ExclusiveAddressUse = false;
                _client.Client.SetSocketOption(SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress, true);
                _client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _client.Client.ReceiveTimeout = 200;
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "BEFORE-UDP" };
                _thread.Start();
            }
            catch (Exception e)
            {
                _running = false;
                Debug.Log("[CommandLab] UDP bind failed on " + port + ": " + e.Message);
            }
        }

        private void Loop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _client.Receive(ref any);
                    string s = System.Text.Encoding.UTF8.GetString(data);
                    lock (_lock) { _queue.Enqueue(s); }
                }
                catch (SocketException) { /* timeout / closed: loop or exit */ }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { break; }
            }
        }

        // Drain all datagrams received since last call (main thread).
        public List<string> Poll()
        {
            var outList = new List<string>();
            lock (_lock)
            {
                while (_queue.Count > 0) outList.Add(_queue.Dequeue());
            }
            if (_running)
            {
                lock (s_webLock)
                {
                    while (s_webQueue.Count > 0) outList.Add(s_webQueue.Dequeue());
                }
            }
            TotalReceived += outList.Count;
            return outList;
        }

        public void StopListening()
        {
            _running = false;
            try { _client?.Close(); } catch { }
            try { _thread?.Join(300); } catch { }
            _client = null;
            _thread = null;
        }

        private void OnDisable() => StopListening();
        private void OnDestroy() => StopListening();
    }
}
