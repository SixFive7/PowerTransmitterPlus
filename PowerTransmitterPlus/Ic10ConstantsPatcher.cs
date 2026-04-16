using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Motherboards;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace PowerTransmitterPlus
{
    // Teaches the IC10 / MIPS compiler to recognize our LogicType names.
    // The compiler resolves tokens like "MicrowaveSourceDraw" to a numeric
    // constant by scanning ProgrammableChip.AllConstants, a public static
    // Constant[] array where each Constant has (Literal, Description, Value).
    //
    // Also extends the syntax-highlighting entries in
    // ProgrammableChip.InternalEnums so that custom LogicType names render
    // with the correct color on in-game screens instead of falling through
    // to the default (red) text color.
    //
    // Pattern lifted from Stationeers Logic Extended (ThunderDuck): one-time
    // reflection write that appends our entries to the existing array. No
    // Harmony patch needed for this step; IC10 reads the array dynamically.
    internal static class Ic10ConstantsPatcher
    {
        private static bool _applied;

        internal static void Apply()
        {
            if (_applied) return;
            _applied = true;

            try
            {
                var field = typeof(ProgrammableChip).GetField("AllConstants",
                    BindingFlags.Static | BindingFlags.Public);
                if (field == null)
                {
                    PowerTransmitterPlusPlugin.Log.LogError(
                        "ProgrammableChip.AllConstants not found, IC10 names disabled");
                    return;
                }

                var existing = (ProgrammableChip.Constant[])field.GetValue(null);
                var additions = new List<ProgrammableChip.Constant>();
                foreach (var t in LogicTypeRegistry.All)
                {
                    additions.Add(new ProgrammableChip.Constant(t.Name, t.Description, (double)t.Value, true));
                }

                var merged = new ProgrammableChip.Constant[existing.Length + additions.Count];
                existing.CopyTo(merged, 0);
                for (int i = 0; i < additions.Count; i++)
                    merged[existing.Length + i] = additions[i];

                field.SetValue(null, merged);

                PowerTransmitterPlusPlugin.Log.LogInfo(
                    $"Registered {additions.Count} IC10 constants ({existing.Length} -> {merged.Length})");
            }
            catch (Exception e)
            {
                PowerTransmitterPlusPlugin.Log.LogError($"Ic10ConstantsPatcher failed: {e}");
            }

            ExtendSyntaxHighlighting();
        }

        // ProgrammableChip.InternalEnums holds IScriptEnum instances that
        // Localization.ParseScript iterates to wrap known tokens in <color>
        // tags. ScriptEnum<LogicType> (bare names like "MicrowaveSourceDraw")
        // and BasicEnum<LogicType> (dotted names like "LogicType.Microwave...")
        // both snapshot Enum.GetValues/GetNames at construction, so our
        // runtime-added values are missing. Extend their private _types and
        // _names arrays via reflection.
        private static void ExtendSyntaxHighlighting()
        {
            try
            {
                var enumsField = typeof(ProgrammableChip).GetField("InternalEnums",
                    BindingFlags.Static | BindingFlags.Public);
                if (enumsField == null)
                {
                    PowerTransmitterPlusPlugin.Log.LogWarning(
                        "ProgrammableChip.InternalEnums not found, screen syntax highlighting won't cover custom types");
                    return;
                }

                var list = (IList)enumsField.GetValue(null);
                int patched = 0;
                foreach (var entry in list)
                {
                    var entryType = entry.GetType();
                    if (!entryType.IsGenericType) continue;

                    var genericArgs = entryType.GetGenericArguments();
                    if (genericArgs.Length != 1 || genericArgs[0] != typeof(LogicType)) continue;

                    // This entry is ScriptEnum<LogicType> or BasicEnum<LogicType>.
                    bool isBasicEnum = entryType.Name.StartsWith("BasicEnum");
                    ExtendScriptEnumEntry(entry, entryType, isBasicEnum);
                    patched++;
                }

                if (patched > 0)
                    PowerTransmitterPlusPlugin.Log.LogInfo(
                        $"Extended {patched} InternalEnums entries for syntax highlighting");
                else
                    PowerTransmitterPlusPlugin.Log.LogWarning(
                        "No ScriptEnum<LogicType>/BasicEnum<LogicType> found in InternalEnums");
            }
            catch (Exception e)
            {
                PowerTransmitterPlusPlugin.Log.LogError(
                    $"ExtendSyntaxHighlighting failed: {e}");
            }
        }

        private static void ExtendScriptEnumEntry(object entry, Type entryType, bool isBasicEnum)
        {
            var typesField = entryType.GetField("_types",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var namesField = entryType.GetField("_names",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (typesField == null || namesField == null)
            {
                PowerTransmitterPlusPlugin.Log.LogWarning(
                    $"_types/_names not found on {entryType.Name}");
                return;
            }

            var oldTypes = (LogicType[])typesField.GetValue(entry);
            var oldNames = (string[])namesField.GetValue(entry);
            var additions = LogicTypeRegistry.All;

            int n = oldTypes.Length + additions.Count;
            var newTypes = new LogicType[n];
            var newNames = new string[n];
            Array.Copy(oldTypes, newTypes, oldTypes.Length);
            Array.Copy(oldNames, newNames, oldNames.Length);

            // BasicEnum<LogicType> prefixes names with "LogicType." so
            // dotted syntax like "LogicType.MicrowaveSourceDraw" highlights.
            for (int i = 0; i < additions.Count; i++)
            {
                int idx = oldTypes.Length + i;
                newTypes[idx] = additions[i].AsLogicType;
                newNames[idx] = isBasicEnum
                    ? "LogicType." + additions[i].Name
                    : additions[i].Name;
            }

            typesField.SetValue(entry, newTypes);
            namesField.SetValue(entry, newNames);
        }
    }
}
