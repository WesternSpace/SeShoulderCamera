using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using System;
using VRage.Game.Utils;
using VRageMath;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyCharacter), nameof(MyCharacter.UpdateShootDirection))]
internal class MyCharacter_UpdateShootDirection_Patch
{
    // Makes weapons shoot towards point marked by crosshair when not in look cam.
    private static void Prefix(MyCharacter __instance, ref bool shootStraight)
    {
        MySector.MainCamera.Zoom.ApplyToFov = true;
        MySector.MainCamera.Zoom.SetZoom(__instance.ZoomMode == MyZoomModeEnum.Classic ? MyCameraZoomOperationType.ZoomingOut : MyCameraZoomOperationType.ZoomingIn);


        if (__instance.ZoomMode == MyZoomModeEnum.Classic || __instance.HeadLocalXAngle < -45 && CameraOffsetInjections.ForwardDistance < 2.0f)
        {
            shootStraight = true;
        }
        else
        {
            shootStraight = false;
        }
            
    }
}
