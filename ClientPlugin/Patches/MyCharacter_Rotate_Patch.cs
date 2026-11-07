using HarmonyLib;
using Sandbox.Game.Entities.Character;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VRageMath;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyCharacter), "Rotate")]
internal class MyCharacter_Rotate_Patch
{
    private static void Prefix(MyCharacter __instance, ref Vector2 rotationIndicator)
    {
        if (__instance.IsIronSighted)
            rotationIndicator.Y = 0;
    }
}
