using HarmonyLib;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using Sandbox.ModAPI.Weapons;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;
using static Sandbox.Engine.Physics.MyPhysics;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyThirdPersonSpectator), nameof(MyThirdPersonSpectator.Update))]
internal class CameraOffsetInjections
{
    private const float LEFT_OFFSET = 0.5f;
    private const float RIGHT_OFFSET = 0.45f;
    private const float LERP_SMOOTHNESS = 0.3f;
    private const float MAX_RAYCAST_DISTANCE = 25000.0f;
    private const int RAYCAST_SKIP_INTERVAL = 5;

    public static float CurrentOffset
    {
        get;
        private set;
    }

    static bool UseLeftCam = false;

    public static float ForwardDistance => forwardDistance;

    private static float forwardDistance = 10f;
    private static bool currentlySwitchingSides = false;
    private static Vector3D prevOffset = default;
    private static int framesSinceSwitch = 0;
    private static int raycastSkipCounter = 0;
    

    // The complex nature of this code injection is due to having to work
    // around injections made by the Allen-Wrench/NoForcedCameraZoom plugin.
    private static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var il = instructions.ToList();

        var m_target_field = AccessTools.Field(typeof(MyThirdPersonSpectator), nameof(MyThirdPersonSpectator.m_target));

        var i = il.FindIndex(ci => ci.opcode == OpCodes.Stfld && (FieldInfo)ci.operand == m_target_field);
        il.InsertRange(i, [new CodeInstruction(OpCodes.Pop), new CodeInstruction(OpCodes.Ldloca_S, 4)]);
        il[i += 2] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraOffsetInjections), nameof(Inject)));

        i = il.FindIndex(i, ci => ci.opcode == OpCodes.Stfld && (FieldInfo)ci.operand == m_target_field);
        il.InsertRange(i, [new CodeInstruction(OpCodes.Pop), new CodeInstruction(OpCodes.Ldloca_S, 8)]);
        il[i += 2] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CameraOffsetInjections), nameof(Inject)));

        return il;
    }

    private static void Inject(MyThirdPersonSpectator __instance, in MatrixD headMatrix)
    {
        // Stage 1: Inject offset into where the camera will be rotating around

        currentlySwitchingSides = false;

        if (MyInput.Static.IsNewKeyPressed(MyKeys.B))
        {
            UseLeftCam = !UseLeftCam;
            currentlySwitchingSides = true;
        }

        IMyControllableEntity controlledEntity = MySession.Static.ControlledEntity;

        __instance.m_target = headMatrix.Translation;

        if (MySession.Static?.ControlledEntity is not MyCharacter character
            || character.Closed
            || character.IsDead)
            return;

        __instance.m_target = GetRequiredCameraOffset(in headMatrix);

        // Stage 2: Handle camera rotation


        // Don't proceed if the player is looking around
        if (MyControllerHelper.IsControl(MyControllerHelper.CX_BASE, MyControlsSpace.LOOKAROUND, MyControlStateType.PRESSED, false, false))
            return;



        __instance.ResetViewerAngle(GetRequiredRotationOffset(in __instance, in headMatrix, in controlledEntity));    
    }

    private static Vector3D GetRequiredCameraOffset(in MatrixD matrix)
    {
        var position = matrix.Translation + matrix.Backward * 0.01f;

        Vector3D offset = UseLeftCam ? (matrix.Left * LEFT_OFFSET) : (matrix.Right * RIGHT_OFFSET);

        if (MySession.Static.ControlledEntity?.Entity is MyCharacter localCharacter)
            offset += ApplyItemSpecificOffsets(in localCharacter, in matrix);

        offset = Vector3D.Lerp(prevOffset, offset, LERP_SMOOTHNESS);
        
        prevOffset = offset;

        return position + offset;
    }

    private static Vector3D ApplyItemSpecificOffsets(in MyCharacter character, in MatrixD matrix)
    {
        Vector3D offset = Vector3D.Zero;
        offset += character.IsCrouching ? matrix.Down * 0.55f : default;

        if (character.EquippedTool is null || character.EquippedTool is IMyBlockPlacerBase)
            return offset;

        if (UseLeftCam)
            offset += (character.EquippedTool as IMyGunBaseUser)?.PhysicalItemId.SubtypeName == "AdvancedHandHeldLauncherItem" ? matrix.Right * 0.2f : default;
        else
            offset += (character.EquippedTool as IMyGunBaseUser)?.PhysicalItemId.SubtypeName == "AdvancedHandHeldLauncherItem" ? matrix.Right * 0.2f : default;

        return offset;
    }

    private static Vector2 GetRequiredRotationOffset(in MyThirdPersonSpectator __instance, in MatrixD matrix, in IMyControllableEntity controlledEntity)
    {
        bool cameraSwitched = MyGuiScreenGamePlay_SwitchCamera_Patch.CameraSwitched;

        if (currentlySwitchingSides || cameraSwitched)
        {
            forwardDistance = GetForwardDistance(in matrix);
            framesSinceSwitch = 0;
            raycastSkipCounter = 0;
            __instance.ResetSpring();
            MyGuiScreenGamePlay_SwitchCamera_Patch.CameraSwitched = false;
        }
        else if (framesSinceSwitch < 30)
        {
            if (raycastSkipCounter < RAYCAST_SKIP_INTERVAL)
            {
                raycastSkipCounter++;
            }
            else
            {
                forwardDistance = GetForwardDistance(in matrix);
                raycastSkipCounter = 0;
            }

            __instance.ResetSpring();
            framesSinceSwitch++;
        }

        var headX = controlledEntity.HeadLocalXAngle;
        var headY = controlledEntity.HeadLocalYAngle;

        headY = MathHelper.ToDegrees(MathHelper.WrapAngle(MathHelper.ToRadians(headY)));

        if (forwardDistance < 1.0f)
            return new Vector2(headX, headY);
  
        float yHeading = (UseLeftCam ? -LEFT_OFFSET : RIGHT_OFFSET) / forwardDistance;

        float xHeading = (controlledEntity as MyCharacter)?.JetpackRunning ?? false ? 0 : headX;

        float lerpFactor = cameraSwitched ? 1.0f : LERP_SMOOTHNESS;

        CurrentOffset = yHeading;

        return Vector2.Lerp(new Vector2(headX, (controlledEntity as MyCharacter)?.IsIronSighted ?? false ? MathHelper.ToDegrees(yHeading) : headY), new Vector2(xHeading, MathHelper.ToDegrees(yHeading)), lerpFactor);
    }

    private static float GetForwardDistance(in MatrixD matrix)
    {
        IHitInfo hitInfo = MyPhysics.CastRay(matrix.Translation, matrix.Translation + matrix.Forward * MAX_RAYCAST_DISTANCE, CollisionLayers.CollisionLayerWithoutCharacter);
        if (hitInfo != null)
            return MAX_RAYCAST_DISTANCE * hitInfo.Fraction;
        else
            return MAX_RAYCAST_DISTANCE;
    }
}