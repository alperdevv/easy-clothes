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

    // Frames the points from the current yaw and pitch: centred on what the camera sees of them, with
    // the given fraction of the image left free on every side. A few passes settle the perspective.
    public void FitTo(IReadOnlyList<Vector3> points, float aspect, float margin)
    {
        if (points.Count == 0) return;
        var (min, max) = (points[0], points[0]);
        foreach (var p in points) (min, max) = (Vector3.Min(min, p), Vector3.Max(max, p));
        Target = (min + max) / 2;
        Distance = Math.Max(Vector3.Distance(min, max), 0.01f) * 2;
        var tanY = MathF.Tan(FieldOfView / 2) * (1 - 2 * margin);
        var tanX = tanY * aspect;

        for (var pass = 0; pass < 3; pass++)
        {
            var toCamera = Vector3.Normalize(Position - Target);
            var right = Vector3.Normalize(Vector3.Cross(-toCamera, Vector3.UnitZ));
            var up = Vector3.Cross(right, -toCamera);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in points)
            {
                var r = p - Target;
                var depth = Distance - Vector3.Dot(r, toCamera);
                var (x, y) = (Vector3.Dot(r, right) / depth, Vector3.Dot(r, up) / depth);
                (minX, maxX, minY, maxY) = (MathF.Min(minX, x), MathF.Max(maxX, x), MathF.Min(minY, y), MathF.Max(maxY, y));
            }
            Target += (right * (minX + maxX) + up * (minY + maxY)) / 2 * Distance;

            var needed = 0.01f;
            foreach (var p in points)
            {
                var r = p - Target;
                var towards = Vector3.Dot(r, toCamera);
                needed = MathF.Max(needed, MathF.Max(MathF.Abs(Vector3.Dot(r, right)) / tanX, MathF.Abs(Vector3.Dot(r, up)) / tanY) + towards);
            }
            Distance = needed;
        }
        MinDistance = Distance * 0.1f;
        MaxDistance = Distance * 20;
    }

    public Matrix4x4 ViewMatrix => Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);

    public Matrix4x4 ProjectionMatrix(float aspect) =>
        Matrix4x4.CreatePerspectiveFieldOfView(FieldOfView, aspect, Distance * 0.01f, Distance * 10);
}
