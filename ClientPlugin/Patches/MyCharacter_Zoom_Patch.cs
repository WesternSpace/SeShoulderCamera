using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyCharacter), nameof(MyCharacter.Zoom))]
internal class MyCharacter_Zoom_Patch
{
    // Disable forced first person when aiming weapon
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.ToList();
        var i = il.FindIndex(ci => ci.opcode == OpCodes.Ldsfld && (FieldInfo)ci.operand == AccessTools.Field(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.Static)));
        il.RemoveRange(i, 2);

        i = il.FindIndex(ci => ci.opcode == OpCodes.Ldsfld && (FieldInfo)ci.operand == AccessTools.Field(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.Static)));
        il.RemoveRange(i, 2);

        return il;
    }
}
