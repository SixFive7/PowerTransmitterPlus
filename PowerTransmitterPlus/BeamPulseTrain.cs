using UnityEngine;

namespace PowerTransmitterPlus
{
    // Draws scrolling "energy pulses" along the beam via a tiled stripe texture
    // on a per-instance material. Spatial wavelength is held constant in world
    // meters, so a 5m beam and a 200m beam both have the same stripe spacing
    // and the same m/s scroll speed. Intensity (game's VisualizerIntensity)
    // modulates scroll speed only — the beam itself stays at full alpha so
    // "linked" remains visible at a glance.
    internal class BeamPulseTrain : MonoBehaviour
    {
        private LineRenderer _lr;
        private Material _instanceMaterial;
        private float _intensity;

        private void Awake()
        {
            _lr = GetComponent<LineRenderer>();
            if (_lr == null) return;

            // Stretch mode: UV goes 0..1 across the whole line regardless of
            // length. mainTextureScale.x then controls tile count.
            _lr.textureMode = LineTextureMode.Stretch;

            // _lr.material getter clones sharedMaterial on first access and
            // caches the clone on the renderer. We keep a reference so Update
            // can set tiling/offset without re-reading (which would re-clone).
            _instanceMaterial = _lr.material;
            _instanceMaterial.mainTexture = BeamManager.StripeTexture;
        }

        internal void SetIntensity(float intensity)
        {
            _intensity = Mathf.Clamp01(intensity);
        }

        private void Update()
        {
            if (_lr == null || _instanceMaterial == null || !_lr.enabled) return;
            if (_lr.positionCount < 2) return;

            var a = _lr.GetPosition(0);
            var b = _lr.GetPosition(1);
            var distance = Vector3.Distance(a, b);
            if (distance < 0.001f) return;

            var wavelength = Mathf.Max(0.05f, PowerTransmitterPlusPlugin.StripeWavelength?.Value ?? 2f);
            var scrollMps = PowerTransmitterPlusPlugin.ScrollSpeed?.Value ?? 6f;

            // Non-linear intensity ramp (sqrt) so even a tiny power draw
            // produces visible motion — the game's VisualizerIntensity often
            // sits at 0.001-0.05 during modest transmission. sqrt(0.002)=0.045,
            // sqrt(0.01)=0.1, sqrt(1)=1 — preserves high/low differentiation
            // while making low-end pulses perceptible.
            var effective = _intensity > 0f ? Mathf.Sqrt(_intensity) : 0f;

            var tiles = distance / wavelength;
            var offset = -Time.time * effective * scrollMps / wavelength;

            _instanceMaterial.mainTextureScale = new Vector2(tiles, 1f);
            _instanceMaterial.mainTextureOffset = new Vector2(offset, 0f);
        }

        private void OnDestroy()
        {
            // Per-instance material was cloned in Awake; Unity does not auto-free
            // it when the GameObject dies. Destroy explicitly to avoid a leak
            // each time a transmitter is removed.
            if (_instanceMaterial != null) Destroy(_instanceMaterial);
        }
    }
}
