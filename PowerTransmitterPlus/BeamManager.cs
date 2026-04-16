using Assets.Scripts.Objects.Electrical;
using System.Collections.Generic;
using UnityEngine;

namespace PowerTransmitterPlus
{
    // Public surface: SetLineIntensity, RefreshIfVisible. Both are safe from
    // any thread — work is enqueued onto the main thread dispatcher.
    internal static class BeamManager
    {
        private static readonly Dictionary<PowerTransmitter, BeamLine> Beams =
            new Dictionary<PowerTransmitter, BeamLine>();

        private static Material _sharedMaterial;
        private static Texture2D _stripeTexture;

        // Procedural 1D stripe texture for the pulse train. Repeat-wrapped so
        // LineRenderer UV tiling via mainTextureScale.x gives one cosine period
        // per stripe. Brightness floor = StripeTroughBrightness; peak = 1.0.
        // Regenerated lazily on first access; tied to current config value.
        internal static Texture2D StripeTexture
        {
            get
            {
                if (_stripeTexture != null) return _stripeTexture;

                const int width = 32;
                var tex = new Texture2D(width, 1, TextureFormat.RGBA32, mipChain: false)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                var trough = Mathf.Clamp01(PowerTransmitterPlusPlugin.StripeTroughBrightness?.Value ?? 0.5f);
                for (int i = 0; i < width; i++)
                {
                    float t = i / (float)width;
                    // 0.5 + 0.5*cos(2πt) = 1 at t=0, 0 at t=0.5, 1 at t=1
                    float wave = 0.5f + 0.5f * Mathf.Cos(t * 2f * Mathf.PI);
                    float b = Mathf.Lerp(trough, 1f, wave);
                    tex.SetPixel(i, 0, new Color(b, b, b, 1f));
                }
                tex.Apply();
                _stripeTexture = tex;
                return _stripeTexture;
            }
        }

        internal static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial != null) return _sharedMaterial;

                var shaderName = PowerTransmitterPlusPlugin.ShaderName?.Value;
                var shader = (!string.IsNullOrEmpty(shaderName) ? Shader.Find(shaderName) : null)
                             ?? Shader.Find("Legacy Shaders/Particles/Additive")
                             ?? Shader.Find("Particles/Additive")
                             ?? Shader.Find("Sprites/Default")
                             ?? Shader.Find("Hidden/Internal-Colored");
                _sharedMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                return _sharedMaterial;
            }
        }

        // Beam RGB at full intensity. Uses effective values (synced from host
        // when Enforce Visual Sync is on, local config otherwise).
        internal static Color BeamColor
        {
            get
            {
                var hex = BeamVisualConfigSync.GetEffectiveBeamColorHex();
                if (!ColorUtility.TryParseHtmlString("#" + hex, out var c)) c = new Color(0f, 0.049f, 1f, 1f);

                var boost = BeamVisualConfigSync.GetEffectiveEmissionIntensity();
                if (boost > 0f && boost != 1f)
                {
                    c = new Color(c.r * boost, c.g * boost, c.b * boost, c.a);
                }
                return c;
            }
        }

        // Force all existing beams to be destroyed so they're recreated with
        // current visual settings on the next SetLineIntensity call.
        internal static void InvalidateAllBeams()
        {
            MainThreadDispatcher.Enqueue(InvalidateAllBeamsOnMain);
        }

        private static void InvalidateAllBeamsOnMain()
        {
            foreach (var beam in Beams.Values)
            {
                if (beam != null && !beam.IsDestroyed) beam.Destroy();
            }
            Beams.Clear();
        }

        // Primary signal. VisualizerIntensity (0..1) drives both on/off and
        // the beam's alpha-based dimming, matching vanilla's power-level fade.
        internal static void SetLineIntensity(PowerTransmitter transmitter, float intensity)
        {
            if (transmitter == null) return;
            MainThreadDispatcher.Enqueue(() => SetLineIntensityOnMain(transmitter, intensity));
        }

        internal static void RefreshIfVisible(PowerTransmitter transmitter)
        {
            if (transmitter == null) return;
            MainThreadDispatcher.Enqueue(() => RefreshIfVisibleOnMain(transmitter));
        }

        private static void SetLineIntensityOnMain(PowerTransmitter transmitter, float intensity)
        {
            if (transmitter == null) return;

            if (intensity <= 0f)
            {
                if (Beams.TryGetValue(transmitter, out var existing) && existing != null && !existing.IsDestroyed)
                    existing.Hide();
                return;
            }

            var receiver = transmitter.LinkedReceiver;
            if (receiver == null || transmitter.RayTransform == null || receiver.RayTransform == null)
            {
                if (Beams.TryGetValue(transmitter, out var existing) && existing != null && !existing.IsDestroyed)
                    existing.Hide();
                return;
            }

            if (!Beams.TryGetValue(transmitter, out var beam) || beam == null || beam.IsDestroyed)
            {
                beam = new BeamLine(transmitter);
                Beams[transmitter] = beam;
            }

            beam.SetIntensity(intensity);
            if (!beam.IsVisible) beam.Show();
        }

        private static void RefreshIfVisibleOnMain(PowerTransmitter transmitter)
        {
            if (Beams.TryGetValue(transmitter, out var beam) && beam != null && !beam.IsDestroyed && beam.IsVisible)
                beam.Refresh();
        }
    }
}
