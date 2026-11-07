using HarmonyLib;
using Sandbox.Game.Entities.Character.Components;
using VRageMath;

namespace ClientPlugin.Patches;

// Runs from MoveAndRotateInternal while the pack is on, including the ALT path.
// The gameplay screen zeros mouse rotation before that path stores indicators,
// so look comes from FlightCamera's stash, not rotationIndicator.
[HarmonyPatch(typeof(MyCharacterJetpackComponent), nameof(MyCharacterJetpackComponent.MoveAndRotate))]
internal class MyCharacterJetpackComponent_MoveAndRotate_Patch
{
    private static void Prefix(MyCharacterJetpackComponent __instance, ref Vector3 moveIndicator, ref Vector2 rotationIndicator, ref float roll, bool canRotate)
    {
        var character = __instance.Character;
        if (!FlightCamera.Prepare(character))
            return;

        FlightCamera.ApplyStashedLook(character);
        Vector3D worldWish = FlightCamera.WorldWish(character, moveIndicator);
        FlightCamera.SteerBody(character, moveIndicator, canRotate);
        moveIndicator = FlightCamera.ToBodyLocal(character, worldWish);
        rotationIndicator = Vector2.Zero;
        roll = 0f;
    }
}
