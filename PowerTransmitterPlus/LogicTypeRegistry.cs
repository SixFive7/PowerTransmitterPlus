using Assets.Scripts.Objects.Motherboards;
using System.Collections.Generic;

namespace PowerTransmitterPlus
{
    // Our three custom LogicType slots. Range 6571-6573 sits well outside vanilla
    // (0-349) and SLE (1000-1830). LogicType is a ushort, so any value up to
    // 65535 is legal at runtime — we just need to avoid collisions with other
    // mods that might pick the same numbers.
    //
    // Reserve 6571-6599 as our band for future readouts.
    internal static class LogicTypeRegistry
    {
        internal const ushort SourceDrawValue = 6571;
        internal const ushort DestinationDrawValue = 6572;
        internal const ushort TransmissionLossValue = 6573;
        internal const ushort EfficiencyValue = 6574;

        internal static readonly LogicType MicrowaveSourceDraw = (LogicType)SourceDrawValue;
        internal static readonly LogicType MicrowaveDestinationDraw = (LogicType)DestinationDrawValue;
        internal static readonly LogicType MicrowaveTransmissionLoss = (LogicType)TransmissionLossValue;
        internal static readonly LogicType MicrowaveEfficiency = (LogicType)EfficiencyValue;

        internal class CustomLogicType
        {
            public string Name;
            public ushort Value;
            public string Description;

            public LogicType AsLogicType => (LogicType)Value;
        }

        // Order is preserved for tablet display.
        internal static readonly List<CustomLogicType> All = new List<CustomLogicType>
        {
            new CustomLogicType
            {
                Name = "MicrowaveSourceDraw",
                Value = SourceDrawValue,
                Description = "Watts the microwave power transmitter is pulling from its source cable network. Equals delivered power times the per-kilometer cost multiplier (server-authoritative; clients see the host's configured value).",
            },
            new CustomLogicType
            {
                Name = "MicrowaveDestinationDraw",
                Value = DestinationDrawValue,
                Description = "Watts being delivered to the receiver's downstream cable network. Equals the wireless link's actual throughput. Mirrors PowerActual but is named for clarity in the microwave context.",
            },
            new CustomLogicType
            {
                Name = "MicrowaveTransmissionLoss",
                Value = TransmissionLossValue,
                Description = "Watts lost to distance overhead. Equals MicrowaveSourceDraw minus MicrowaveDestinationDraw. Zero when the link is at zero distance (or when k=0).",
            },
            new CustomLogicType
            {
                Name = "MicrowaveEfficiency",
                Value = EfficiencyValue,
                Description = "Ratio of delivered power to source draw, 0..1. Equals 1/(1 + k * distance_km). 1.0 at zero distance or k=0; drops toward 0 as distance grows. Returns 0 when no transmission is happening.",
            },
        };

        internal static readonly Dictionary<ushort, CustomLogicType> ByValue = BuildIndex();

        private static Dictionary<ushort, CustomLogicType> BuildIndex()
        {
            var d = new Dictionary<ushort, CustomLogicType>();
            foreach (var t in All) d[t.Value] = t;
            return d;
        }

        internal static bool IsCustom(LogicType t) => ByValue.ContainsKey((ushort)t);

        internal static bool TryGetName(LogicType t, out string name)
        {
            if (ByValue.TryGetValue((ushort)t, out var info))
            {
                name = info.Name;
                return true;
            }
            name = null;
            return false;
        }
    }
}
