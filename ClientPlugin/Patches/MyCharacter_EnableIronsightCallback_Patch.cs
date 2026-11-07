using HarmonyLib;
using Sandbox.Game.Entities.Character;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyCharacter), nameof(MyCharacter.EnableIronsightCallback))]
internal class MyCharacter_EnableIronsightCallback_Patch
{
    // Disable more forced camera changes when aiming weapon.
    private static void Prefix(MyCharacter __instance, ref bool changeCamera, ref bool forceChangeCamera)
    {
        changeCamera = false;
        forceChangeCamera = false;
    }
}
