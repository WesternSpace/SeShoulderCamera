using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Engine.Utils;
using VRageMath;

namespace ClientPlugin.Patches;

// Replaces the head orientation at the store, before Update builds the boom
// and before it assigns m_position from that boom.
[HarmonyPatch(typeof(MyThirdPersonSpectator), nameof(MyThirdPersonSpectator.Update))]
internal class MyThirdPersonSpectator_FlightOrientation_Patch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo orientation = AccessTools.Field(typeof(MyThirdPersonSpectator), nameof(MyThirdPersonSpectator.m_targetOrientation));
        MethodInfo select = AccessTools.Method(typeof(FlightCamera), nameof(FlightCamera.SelectOrientation));
        var il = instructions.ToList();

        for (int i = il.Count - 1; i >= 0; i--)
        {
            if (il[i].opcode != OpCodes.Stfld || (FieldInfo)il[i].operand != orientation)
                continue;

            var call = new CodeInstruction(OpCodes.Call, select);
            call.labels.AddRange(il[i].labels);
            il[i].labels.Clear();
            il.Insert(i, call);
        }

        return il;
    }
}
