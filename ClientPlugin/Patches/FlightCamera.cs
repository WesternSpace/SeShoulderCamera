using System;
using Sandbox.Game;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRage.Input;
using VRageMath;

namespace ClientPlugin.Patches;

// Vanilla flight camera unless ALT / lookaround is held.
// While held, this basis is the view. Mouse and roll turn the basis only.
// The body is that same frame, yawed about the view up toward the horizontal stick.
// Pitch and roll therefore match the view at every heading, including facing the camera.
// Releasing ALT drops the basis. It is not restored.
internal static class FlightCamera
{
    private const float VanillaRotationScale = 0.02f;
    private const float BodyCatchup = 0.2f;
    private const float HorizontalEpsilonSq = 1e-6f;

    private static bool HasBasis;
    private static long OwnerId;
    private static MatrixD Basis = MatrixD.Identity;
    private static Vector2 StashedRotation;
    private static float StashedRoll;

    public static Vector3D Left => Basis.Left;
    public static Vector3D Right => Basis.Right;

    public static void Release()
    {
        HasBasis = false;
        OwnerId = 0;
        Basis = MatrixD.Identity;
        StashedRotation = Vector2.Zero;
        StashedRoll = 0f;
    }

    public static void StashLook(Vector2 rotation, float roll)
    {
        StashedRotation = rotation;
        StashedRoll = roll;
    }

    public static bool OwnsView(MyCharacter character)
    {
        if (!IsFlightCharacter(character) || character.IsIronSighted || !IsLookAroundHeld())
        {
            Release();
            return false;
        }

        return true;
    }

    public static bool Prepare(MyCharacter character)
    {
        if (!OwnsView(character))
            return false;

        MatrixD head = character.GetHeadMatrix(includeY: true);
        EnsureFromHead(character, in head);
        if (character.Definition.VerticalPositionFlyingOnly)
            character.HeadLocalXAngle = 0f;
        return HasBasis;
    }

    public static void EnsureFromHead(MyCharacter character, in MatrixD headMatrix)
    {
        if (!HasBasis || OwnerId != character.EntityId)
            Seed(character, headMatrix.GetOrientation());
    }

    public static MatrixD SelectOrientation(MatrixD vanilla)
    {
        MyCharacter character = MySession.Static?.ControlledEntity as MyCharacter;
        if (!OwnsView(character))
            return vanilla;

        if (!HasBasis || OwnerId != character.EntityId)
            Seed(character, vanilla);

        Basis.Translation = Vector3D.Zero;
        return Basis;
    }

    public static void ApplyStashedLook(MyCharacter character)
    {
        ApplyManualLook(character, StashedRotation, StashedRoll);
        StashedRotation = Vector2.Zero;
        StashedRoll = 0f;
    }

    public static void ApplyManualLook(MyCharacter character, Vector2 rotation, float roll)
    {
        if (!HasBasis)
            return;

        MatrixD pitch = MatrixD.Identity;
        MatrixD yaw = MatrixD.Identity;
        MatrixD rollMatrix = MatrixD.Identity;
        float speed = character.RotationSpeed;

        if (Math.Abs(rotation.X) > float.Epsilon)
            pitch = MatrixD.CreateFromAxisAngle(Basis.Right, -rotation.X * speed * VanillaRotationScale);
        if (Math.Abs(rotation.Y) > float.Epsilon)
            yaw = MatrixD.CreateFromAxisAngle(Basis.Up, -rotation.Y * speed * VanillaRotationScale);
        if (Math.Abs(roll) > float.Epsilon)
            rollMatrix = MatrixD.CreateFromAxisAngle(Basis.Forward, roll * VanillaRotationScale);

        Basis = Basis.GetOrientation();
        Basis *= pitch * yaw * rollMatrix;
        Basis.Translation = Vector3D.Zero;
    }

    public static void SteerBody(MyCharacter character, Vector3 control, bool canRotate)
    {
        if (!canRotate || !HasBasis || character.Physics == null || character.ModelCollision == null)
            return;
        if (!TryDesired(character.WorldMatrix, control, out MatrixD desired))
            return;

        MatrixD world = character.WorldMatrix;
        if (Vector3D.Dot(world.Forward, desired.Forward) > 0.9999
            && Vector3D.Dot(world.Up, desired.Up) > 0.9999)
            return;

        MatrixD turned = SlerpOrientation(world.GetOrientation(), desired, BodyCatchup);
        float halfHeight = character.ModelCollision.BoundingBoxSizeHalf.Y;
        Vector3D pivot = character.Physics.GetWorldMatrix().Translation + world.Up * halfHeight;
        turned.Translation = pivot - turned.Up * halfHeight;
        character.WorldMatrix = turned;
        character.ClearShapeContactPoints();
    }

    // World direction from the pre-steer body. Call this before SteerBody.
    // The plane is that body's up. The stick is the view's right and forward.
    public static Vector3D WorldWish(MyCharacter character, Vector3 control)
    {
        MatrixD body = character.WorldMatrix;
        Vector3D up = body.Up;
        if (up.LengthSquared() < 1e-8)
            return new Vector3D(control);
        up = Vector3D.Normalize(up);

        Vector3D world = up * control.Y;
        float horiz = (float)Math.Sqrt(control.X * control.X + control.Z * control.Z);
        if (horiz > 1e-4f && TryBodyPlaneWish(up, control, out Vector3D wish))
            world += wish * horiz;
        return world;
    }

    // Express a world wish in the body axes after SteerBody. Vanilla adds ControlThrust
    // after it has written WorldMatrix, as a body-local force.
    public static Vector3 ToBodyLocal(MyCharacter character, Vector3D world)
    {
        MatrixD body = character.WorldMatrix;
        double right = Vector3D.Dot(world, body.Right);
        double vertical = Vector3D.Dot(world, body.Up);
        double forward = Vector3D.Dot(world, body.Forward);
        return new Vector3((float)right, (float)vertical, (float)-forward);
    }

    private static bool TryDesired(MatrixD world, Vector3 control, out MatrixD desired)
    {
        desired = MatrixD.Identity;
        if (Basis.Up.LengthSquared() < 1e-8)
            return false;

        double psi;
        if (control.X * control.X + control.Z * control.Z >= HorizontalEpsilonSq)
        {
            Vector3D wish = Basis.Right * control.X + Basis.Forward * (-control.Z);
            if (!TryYaw(Basis.Forward, wish, out psi))
                return false;
        }
        else
        {
            Vector3D flat = world.Forward - Basis.Up * Vector3D.Dot(world.Forward, Basis.Up);
            if (flat.LengthSquared() < 1e-8)
                return false;
            flat.Normalize();
            if (!TryYaw(Basis.Forward, flat, out psi))
                return false;
        }

        desired = Basis.GetOrientation();
        desired *= MatrixD.CreateFromAxisAngle(Basis.Up, psi);
        desired.Translation = Vector3D.Zero;
        return true;
    }

    private static bool TryYaw(Vector3D forward, Vector3D wish, out double psi)
    {
        psi = 0.0;
        double sine = Vector3D.Dot(Basis.Up, Vector3D.Cross(forward, wish));
        double cosine = MathHelper.Clamp(Vector3D.Dot(forward, wish), -1.0, 1.0);
        if (sine * sine + cosine * cosine < 1e-8)
            return false;
        psi = Math.Atan2(sine, cosine);
        return true;
    }

    private static bool TryBodyPlaneWish(Vector3D planeUp, Vector3 control, out Vector3D wish)
    {
        wish = default;
        Vector3D up = planeUp;
        if (up.LengthSquared() < 1e-8)
            return false;
        up = Vector3D.Normalize(up);

        wish = Basis.Right * control.X + Basis.Forward * (-control.Z);
        wish -= up * Vector3D.Dot(wish, up);
        if (wish.LengthSquared() < 1e-8)
            return false;

        wish.Normalize();
        return true;
    }

    private static bool IsFlightCharacter(MyCharacter character)
    {
        if (character == null || character.Closed || character.IsDead || !character.JetpackRunning)
            return false;
        if (MySession.Static?.ControlledEntity != character)
            return false;
        if (character.IsInFirstPersonView || character.ForceFirstPersonCamera)
            return false;
        if (character.Definition == null || character.Physics == null || character.ModelCollision == null)
            return false;
        return true;
    }

    private static bool IsLookAroundHeld()
    {
        if (MyInput.Static != null && MyInput.Static.IsLookAround())
            return true;

        return MyControllerHelper.IsControl(
            MyControllerHelper.CX_BASE,
            MyControlsSpace.LOOKAROUND,
            MyControlStateType.PRESSED,
            false,
            false);
    }

    private static void Seed(MyCharacter character, MatrixD orientation)
    {
        Basis = orientation.GetOrientation();
        Basis.Translation = Vector3D.Zero;
        OwnerId = character.EntityId;
        HasBasis = true;
    }

    private static MatrixD SlerpOrientation(MatrixD from, MatrixD to, float amount)
    {
        Matrix fromMatrix = Matrix.CreateFromDir((Vector3)from.Forward, (Vector3)from.Up);
        Matrix toMatrix = Matrix.CreateFromDir((Vector3)to.Forward, (Vector3)to.Up);
        Quaternion a = Quaternion.CreateFromRotationMatrix(fromMatrix);
        Quaternion b = Quaternion.CreateFromRotationMatrix(toMatrix);
        if (Quaternion.Dot(a, b) < 0f)
            b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);

        Matrix spun = Matrix.CreateFromQuaternion(Quaternion.Slerp(a, b, amount));
        return MatrixD.CreateFromDir((Vector3D)spun.Forward, (Vector3D)spun.Up);
    }
}
