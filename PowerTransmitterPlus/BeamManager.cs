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

        // Beam RGB at full intensity. Alpha is driven separately by SetLineIntensity
        // to mirror the vanilla SetMaterialPropertiesForIntensity behavior.
        internal static Color BeamColor
        {
            get
            {
                var hex = PowerTransmitterPlusPlugin.BeamColorHex?.Value ?? "000DFF";
                if (!ColorUtility.TryParseHtmlString("#" + hex, out var c)) c = new Color(0f, 0.049f, 1f, 1f);

                var boost = PowerTransmitterPlusPlugin.EmissionIntensity?.Value ?? 1f;
                if (boost > 0f && boost != 1f)
                {
                    c = new Color(c.r * boost, c.g * boost, c.b * boost, c.a);
                }
                return c;
            }
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
