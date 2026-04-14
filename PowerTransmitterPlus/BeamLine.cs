using Assets.Scripts.Objects.Electrical;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerTransmitterPlus
{
    // One LineRenderer per transmitter. World-space positions, parented to the
    // transmitter GameObject so destruction of the transmitter also destroys
    // the beam. Positions are written on Show() and Refresh() only.
    //
    // Color/width/shader come from BeamManager + config. No runtime reflection.
    internal class BeamLine
    {
        private readonly PowerTransmitter _transmitter;
        private readonly GameObject _gameObject;
        private readonly LineRenderer _lineRenderer;

        public bool IsVisible { get; private set; }
        public bool IsDestroyed => _gameObject == null;

        public BeamLine(PowerTransmitter transmitter)
        {
            _transmitter = transmitter;

            _gameObject = new GameObject("PowerTransmitterPlus_Line");
            _gameObject.transform.SetParent(transmitter.transform, worldPositionStays: false);

            _lineRenderer = _gameObject.AddComponent<LineRenderer>();
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 2;
            _lineRenderer.sharedMaterial = BeamManager.SharedMaterial;

            var color = BeamManager.BeamColor;
            // Initial alpha is 0 — SetIntensity(power) will drive it up to match
            // the game's current VisualizerIntensity. Prevents a full-bright
            // flash during the one frame between Show() and the first intensity update.
            color.a = 0f;
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;

            var width = PowerTransmitterPlusPlugin.BeamWidth?.Value ?? 0.1f;
            if (width <= 0f) width = 0.1f;
            _lineRenderer.startWidth = width;
            _lineRenderer.endWidth = width;

            _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lineRenderer.lightProbeUsage = LightProbeUsage.Off;
            _lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _lineRenderer.enabled = false;
        }

        public void Show()
        {
            if (IsDestroyed) return;
            Refresh();
            IsVisible = true;
            _lineRenderer.enabled = true;
        }

        public void Hide()
        {
            IsVisible = false;
            if (IsDestroyed) return;
            _lineRenderer.enabled = false;
        }

        // Drives the LineRenderer vertex alpha with the game's power-level
        // intensity (0..1). On an additive-blend shader, srcAlpha modulates
        // the contribution — alpha=0.5 halves brightness, matching vanilla's
        // "beam dims with low power" behavior.
        public void SetIntensity(float intensity)
        {
            if (IsDestroyed) return;
            var color = BeamManager.BeamColor;
            // TEMP: force full alpha so the beam always renders at 100% regardless
            // of actual power level. Revert to `Mathf.Clamp01(intensity)` to
            // restore vanilla-style power-level dimming.
            color.a = 1f;
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;
        }

        public void Refresh()
        {
            if (IsDestroyed) return;
            var receiver = _transmitter != null ? _transmitter.LinkedReceiver : null;
            if (_transmitter == null || receiver == null
                || _transmitter.RayTransform == null
                || receiver.RayTransform == null)
            {
                Hide();
                return;
            }

            _lineRenderer.SetPosition(0, _transmitter.RayTransform.position);
            _lineRenderer.SetPosition(1, receiver.RayTransform.position);
        }
    }
}
