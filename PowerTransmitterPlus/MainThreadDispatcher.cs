using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace PowerTransmitterPlus
{
    // Stationeers drives power-tick code (PowerTick.ApplyState → ReceivePower →
    // VisualizerIntensity setter) on a ThreadPool worker via UniTask's
    // SwitchToThreadPoolAwaitable. Our Harmony postfixes inherit that thread,
    // so any call to a Unity API (new GameObject, Shader.Find, Transform.position,
    // LineRenderer.SetPosition) hard-crashes the native Unity player.
    //
    // This dispatcher parks a queue on a DontDestroyOnLoad GameObject, drained
    // in Update() on the main thread. Patches enqueue closures from any thread,
    // the closure body runs safely on the main thread one frame later.
    internal class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static MainThreadDispatcher _instance;

        internal static void Init()
        {
            if (_instance != null) return;
            var go = new GameObject("PowerTransmitterPlus_MainThreadDispatcher");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MainThreadDispatcher>();
        }

        internal static void Enqueue(Action action)
        {
            if (action == null || _instance == null) return;
            Queue.Enqueue(action);
        }

        private void Update()
        {
            while (Queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { PowerTransmitterPlusPlugin.Log?.LogError($"dispatcher action threw: {e}"); }
            }
        }
    }
}
