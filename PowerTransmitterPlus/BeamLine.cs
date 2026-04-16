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
        private readonly BeamPulseTrain _pulseTrain;

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

            // Beam is always at full alpha when visible, so a player can always
            // see where a link exists. The scrolling pulse train (below) carries
            // the power-level information via speed of motion, not dimming.
            var color = BeamManager.BeamColor;
            color.a = 1f;
            _lineRenderer.startColor = color;
            _lineRenderer.endColor = color;

            var width = BeamVisualConfigSync.GetEffectiveBeamWidth();
            if (width <= 0f) width = 0.1f;
            _lineRenderer.startWidth = width;
            _lineRenderer.endWidth = width;

            _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lineRenderer.lightProbeUsage = LightProbeUsage.Off;
            _lineRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _lineRenderer.enabled = false;

            // AddComponent must happen after the LineRenderer exists — the
            // pulse train reads it in its own Awake via GetComponent.
            _pulseTrain = _gameObject.AddComponent<BeamPulseTrain>();
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

        public void Destroy()
        {
            IsVisible = false;
            if (_gameObject != null) Object.Destroy(_gameObject);
        }

        // Forwards the game's power-level intensity (0..1) to the pulse train,
        // which turns it into a scroll speed. The beam's own alpha is held at
        // 1 so the line remains fully visible whenever the link exists — the
        // pulse train is the only power-level indicator.
        public void SetIntensity(float intensity)
        {
            if (IsDestroyed) return;
            _pulseTrain?.SetIntensity(intensity);
        }

        // The vanilla RayTransform on each dish sits slightly inside the visible
        // geometry, so a beam drawn RayTransform-to-RayTransform looks short at
        // both ends. Push each endpoint outward along the beam axis by a fixed
        // amount so the line visually covers the emitter tips.
        private const float EndpointExtensionMeters = 1.0f;

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

            var start = _transmitter.RayTransform.position;
            var end = receiver.RayTransform.position;
            var delta = end - start;
            if (delta.sqrMagnitude > 1e-6f)
            {
                var axis = delta.normalized;
                start -= axis * EndpointExtensionMeters;
                end += axis * EndpointExtensionMeters;
            }

            _lineRenderer.SetPosition(0, start);
            _lineRenderer.SetPosition(1, end);
        }
    }
}
