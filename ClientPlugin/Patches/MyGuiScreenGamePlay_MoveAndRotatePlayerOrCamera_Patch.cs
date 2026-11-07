using HarmonyLib;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using VRage.Game;
using VRage.Input;
using VRageMath;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyGuiScreenGamePlay), nameof(MyGuiScreenGamePlay.MoveAndRotatePlayerOrCamera))]
internal class MyGuiScreenGamePlay_MoveAndRotatePlayerOrCamera_Patch
{
    // Enables camera relative player movement instead of character relative when in free look mode
    // Currently only applies for character movementb

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.ToList();
        var i = il.FindIndex(ci => ci.opcode == OpCodes.Callvirt && (MethodInfo)ci.operand == AccessTools.PropertyGetter(typeof(IMyInput), nameof(IMyInput.IsJoystickLastUsed))) + 2;
        il[i++] = new CodeInstruction(OpCodes.Ldarg_0);
        il[i++] = new CodeInstruction(OpCodes.Ldloca_S, (byte)7);
        il[i++] = new CodeInstruction(OpCodes.Ldloca_S, (byte)6);
        il.Insert(i++, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MyGuiScreenGamePlay_MoveAndRotatePlayerOrCamera_Patch), nameof(Inject))));

        return il;
    }

    private static void Prefix()
    {
        if (MyInput.Static == null)
            FlightCamera.StashLook(Vector2.Zero, 0f);
        else
            FlightCamera.StashLook(
                MyInput.Static.GetRotation(),
                MyInput.Static.GetRoll());
    }

    private static void Inject(MyGuiScreenGamePlay __instance, ref Vector3 moveDirection, ref Vector2 rotation)
    {

        if (MySession.Static.ControlledEntity is MyCharacter character)
        {
            if (character.JetpackRunning)
                return;

            double horizLengthSq = moveDirection.X * moveDirection.X + moveDirection.Z * moveDirection.Z;
            if (horizLengthSq > 0 || character.IsIronSighted)
            {
                double targetAngle = MathHelper.ToDegrees(Math.Atan2(moveDirection.X, -moveDirection.Z));

                if (character.IsIronSighted)
                    targetAngle = CameraOffsetInjections.CurrentOffset;


                if (targetAngle < 0)
                {
                    targetAngle += 360;
                }
                RotateToTargetAngle(character, targetAngle, ref moveDirection);
            }
        }
    }

    private static void RotateToTargetAngle(MyCharacter character, double targetAngle, ref Vector3 moveDirection)
    {
        double headingClamped = MathHelper.WrapAngle(MathHelper.ToRadians(character.HeadLocalYAngle));
        MathHelper.LimitRadians2PI(ref headingClamped);
        headingClamped = MathHelperD.ToDegrees(headingClamped); float delta = (float)((targetAngle - headingClamped) % 360f);

        if (delta > 180f)
            delta -= 360f;
        else if (delta < -180f)
            delta += 360f;

        const double epsilon = 1.0;  // Deadzone: Stop if within this many degrees
        const double maxTurnSpeedDegPerSec = 10000.0;  // Max turn speed; tune for feel (higher = faster)

        if (Math.Abs(delta) > epsilon)
        {
            // Proportional turn rate: Scales with delta for smooth easing
            double turnRateDegPerSec;


            if ((MySession.Static.ControlledEntity as MyCharacter).IsIronSighted)
            {
                // AIMING MODE = "player relative" feel (RDR2 ADS style)
                // Instant snap to camera direction + movement locked to body facing
                // No heavy small-turn inertia — you want precise control while aiming
                if (Math.Abs(delta) < 45f)
                    turnRateDegPerSec = delta * 22f;      // very responsive small corrections
                else
                    turnRateDegPerSec = 990f + (delta - 45f) * 32f;   // up to ~2800°/s on big flicks
            }
            else{
                if (Math.Abs(delta) < 55f)
                {
                    turnRateDegPerSec = delta * 3.5f;
                }
                else if (Math.Abs(delta) < 125f)
                {
                    turnRateDegPerSec = 357f + (delta - 55f) * 4.8f;
                }
                else
                {
                    turnRateDegPerSec = 1020f + (delta - 125f) * 8.8f;
                }
            }
            

            // Clamp to max speed
            if (Math.Abs(turnRateDegPerSec) > maxTurnSpeedDegPerSec)
            {
                turnRateDegPerSec = Math.Sign(turnRateDegPerSec) * maxTurnSpeedDegPerSec;
            }
            RotateCharacterOnly(character, -(float)turnRateDegPerSec);
        }

        if ((MySession.Static.ControlledEntity as MyCharacter).IsIronSighted)
            return;

        moveDirection.Z = -(float)Math.Sqrt(moveDirection.X * moveDirection.X + moveDirection.Z * moveDirection.Z);
        moveDirection.X = 0;
    }



    private static void RotateCharacterOnly(MyCharacter character, float turnRateDegPerSec)
    {
        float rotationThisFrameRad = MathHelper.ToRadians(turnRateDegPerSec * MyEngineConstants.UPDATE_STEP_SIZE_IN_SECONDS);

        if (character.Physics.CharacterProxy != null)
        {
            MatrixD matrixD = MatrixD.CreateRotationY(rotationThisFrameRad);
            MatrixD matrixD2 = MatrixD.CreateWorld(character.Physics.CharacterProxy.Position, character.Physics.CharacterProxy.Forward, character.Physics.CharacterProxy.Up);
            matrixD2 = matrixD * matrixD2;
            character.Physics.CharacterProxy.SetForwardAndUp(matrixD2.Forward, matrixD2.Up);
        }
        else
        {
            MatrixD matrixD3 = MatrixD.CreateRotationY(rotationThisFrameRad);
            MatrixD matrixD4 = character.WorldMatrix;
            matrixD4 = matrixD3 * matrixD4;
            matrixD4.Translation = character.WorldMatrix.Translation;
            character.PositionComp.SetWorldMatrix(ref matrixD4, null, false, true, true, false, false, false);
        }

        character.HeadLocalYAngle -= (float)MathHelperD.ToDegrees(rotationThisFrameRad);
    }
}
