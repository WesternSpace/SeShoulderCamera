using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;


namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyCharacter), nameof(MyCharacter.IsInFirstPersonView))]
[HarmonyPatch(MethodType.Setter)]
internal class MyCharacter_IsInFirstPersonView_set_Patch
{
    // Disable unaiming on switching back to third person while aiming.
    // Also disables camera Y angle being reset when switching cameras
    private static bool Prefix(MyCharacter __instance, bool __0)
    {
        if (!__0 && !MySession.Static.Settings.Enable3rdPersonView)
        {
            __instance.m_isInFirstPersonView = true;
            return false;
        }

        if (__instance.Definition.EnableFirstPersonView)
        {
            if (__instance.m_actualUpdateFrame > 0UL)
            {
                __instance.m_headLocalYAngle = 0f;
            }
           __instance.m_isInFirstPersonView = __0;
            return false;
        }

        __instance.m_isInFirstPersonView = false;
        return false;
    }
}
