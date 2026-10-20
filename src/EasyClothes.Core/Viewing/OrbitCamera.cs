using System.Numerics;

namespace EasyClothes.Core.Viewing;

public sealed class OrbitCamera
{
    private const float MaxPitch = 1.5f;

    public Vector3 Target { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Distance { get; set; } = 2f;
    public float FieldOfView { get; set; } = MathF.PI / 4;
    public float MinDistance { get; set; } = 0.05f;
    public float MaxDistance { get; set; } = 50f;

    public Vector3 Position => Target + Distance * new Vector3(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        -MathF.Cos(Pitch) * MathF.Cos(Yaw),
        MathF.Sin(Pitch));

    public void Rotate(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, -MaxPitch, MaxPitch);
    }

    public void Pan(float right, float up)
    {
        var forward = Vector3.Normalize(Target - Position);
        var rightAxis = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        var upAxis = Vector3.Cross(rightAxis, forward);
        var scale = Distance * MathF.Tan(FieldOfView / 2);
        Target += (rightAxis * right + upAxis * up) * scale;
    }

    public void Zoom(float factor) =>
        Distance = Math.Clamp(Distance * factor, MinDistance, MaxDistance);

    public void Frame(Vector3 min, Vector3 max)
    {
        Target = (min + max) / 2;
        var radius = Math.Max(Vector3.Distance(min, max) / 2, 0.01f);
        MinDistance = radius * 0.1f;
        MaxDistance = radius * 20;
        Distance = radius / MathF.Sin(FieldOfView / 2) * 1.1f;
    }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);

    public Matrix4x4 ProjectionMatrix(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Distance * 0.01f, Distance * 10);
}
