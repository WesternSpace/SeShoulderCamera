using HarmonyLib;
using ParallelTasks;
using Sandbox.Game.Gui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;


namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.SwitchCamera))]
internal class MyGuiScreenGamePlay_SwitchCamera_Patch
{
    public static bool CameraSwitched = false;
    private static void Postfix()
    {
        CameraSwitched = true;
        MyHud.ScreenEffects.BlackScreenMinimalizeHUD = false;
        MyHud.ScreenEffects.FadeScreen(0f);
        Parallel.Start(delegate
        {
            Thread.Sleep(125);
            MyHud.ScreenEffects.FadeScreen(1f, 0.55f);
        });
    }
}
