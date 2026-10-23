using System.Numerics;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Modeling;

public readonly record struct UvRect(float U0, float V0, float U1, float V1)
{
    public Vector2 At(float u, float v) => new(U0 + u * (U1 - U0), V0 + v * (V1 - V0));
}

// Builds the cap mesh in head-bone space (+X up, +Y towards the face, Z sideways), fitted to the
// freemode head. Shapes are worked out in a cap frame (forward, side, up) and placed at the end.
public static class CapModel
{
    // Fixed texture layout, so painted textures survive any change of shape.
    public static readonly UvRect Crown = new(0, 0, 1, 0.25f);
    public static readonly UvRect Rim = new(0, 0.25f, 1, 0.27f);
    public static readonly UvRect VisorTop = new(0, 0.30f, 0.5f, 0.50f);
    public static readonly UvRect VisorUnder = new(0.5f, 0.30f, 1, 0.50f);
    public static readonly UvRect VisorEdge = new(0, 0.52f, 1, 0.54f);
    public static readonly UvRect ButtonTop = new(0, 0.56f, 0.05f, 0.61f);
    public static readonly UvRect Inner = new(0, 0.62f, 1, 0.87f);

    // Fit to the freemode head (measured on the game's head in bone space), in metres.
    private const float HeadCentreForward = 0.012f;
    private const float BandHeight = 0.08f;
    private const float HalfLength = 0.112f;
    private const float HalfWidth = 0.096f;
    private const float TiltDegrees = 18;
    private const float Thickness = 0.003f;
    private const float VisorThickness = 0.004f;
    private const float VisorHalfAngle = 60 * MathF.PI / 180;

    private const int Around = 48;
    private const int Rings = 14;
    private const int VisorColumns = 40;
    private const int VisorRows = 8;

    public static MeshData Build(CapDesign design)
    {
        var d = design.Clamped();
        var shape = new Shape(d);
        var mesh = new MeshBuilder();

        var outer = CrownGrid(mesh, shape, 0, Crown, outward: true);
        var inner = CrownGrid(mesh, shape, Thickness, Inner, outward: false);
        var rim = mesh.NewPart();
        for (var i = 0; i < Around; i++)
        {
            var (u0, u1) = ((float)i / Around, (float)(i + 1) / Around);
            var a = mesh.Add(rim, mesh.PositionOf(outer[i, 0]), Rim.At(u0, 0));
            var b = mesh.Add(rim, mesh.PositionOf(outer[i + 1, 0]), Rim.At(u1, 0));
            var c = mesh.Add(rim, mesh.PositionOf(inner[i + 1, 0]), Rim.At(u1, 1));
            var e = mesh.Add(rim, mesh.PositionOf(inner[i, 0]), Rim.At(u0, 1));
            mesh.Quad(a, e, c, b);
        }

        if (d.VisorLength > 0) Visor(mesh, shape);
        if (d.Button) Button(mesh, shape);
        return mesh.ToMesh(shape.ToBone);
    }

    // The visor's top surface as a grid of (cap-frame position, distance across the visor 0..1, angle).
    internal static (Vector3[,] Points, float[,] Across, float[,] Angle) VisorGrid(CapDesign design)
    {
        var shape = new Shape(design.Clamped());
        var points = new Vector3[VisorColumns + 1, VisorRows + 1];
        var across = new float[VisorColumns + 1, VisorRows + 1];
        var angle = new float[VisorColumns + 1, VisorRows + 1];
        for (var i = 0; i <= VisorColumns; i++)
        {
            var phi = -VisorHalfAngle + 2 * VisorHalfAngle * i / VisorColumns;
            for (var k = 0; k <= VisorRows; k++)
            {
                var s = (float)k / VisorRows;
                points[i, k] = shape.VisorPoint(phi, s);
                across[i, k] = s;
                angle[i, k] = phi;
            }
        }
        return (points, across, angle);
    }

    // Maps a visor-top point (cap frame) into the visor rectangles: front edge at the top.
    internal static Vector2 VisorUv(UvRect rect, Vector3 p, (float Side, float Back, float Front) extent, bool mirror)
    {
        var side = 0.5f + 0.5f * p.Y / extent.Side;
        if (mirror) side = 1 - side;
        return rect.At(side, (extent.Front - p.X) / (extent.Front - extent.Back));
    }

    internal static (float Side, float Back, float Front) VisorExtent(Vector3[,] points)
    {
        var all = points.Cast<Vector3>().ToList();
        return (all.Max(p => MathF.Abs(p.Y)), all.Min(p => p.X), all.Max(p => p.X));
    }

    private static int[,] CrownGrid(MeshBuilder mesh, Shape shape, float inset, UvRect rect, bool outward)
    {
        var part = mesh.NewPart();
        var grid = new int[Around + 1, Rings];
        for (var j = 0; j < Rings; j++)
        {
            var theta = MathF.PI / 2 * j / Rings;
            for (var i = 0; i <= Around; i++)
                grid[i, j] = mesh.Add(part, shape.CrownPoint(Angle(i), theta, inset), rect.At((float)i / Around, 1 - (float)j / Rings));
        }
        for (var i = 0; i < Around; i++)
        {
            for (var j = 0; j + 1 < Rings; j++)
            {
                if (outward) mesh.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                else mesh.Quad(grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j]);
            }
            var pole = mesh.Add(part, shape.CrownPoint(0, MathF.PI / 2, inset), rect.At((i + 0.5f) / Around, 0));
            if (outward) mesh.Triangle(grid[i, Rings - 1], grid[i + 1, Rings - 1], pole);
            else mesh.Triangle(grid[i, Rings - 1], pole, grid[i + 1, Rings - 1]);
        }
        return grid;
    }

    private static void Visor(MeshBuilder mesh, Shape shape)
    {
        var (points, _, _) = VisorGrid(shape.Design);
        var extent = VisorExtent(points);
        var down = new Vector3(0, 0, -VisorThickness);
        var top = mesh.NewPart();
        var under = mesh.NewPart();
        var edge = mesh.NewPart();
        var topIds = new int[VisorColumns + 1, VisorRows + 1];
        var underIds = new int[VisorColumns + 1, VisorRows + 1];
        for (var i = 0; i <= VisorColumns; i++)
        {
            for (var k = 0; k <= VisorRows; k++)
            {
                var p = points[i, k];
                topIds[i, k] = mesh.Add(top, p, VisorUv(VisorTop, p, extent, mirror: false));
                underIds[i, k] = mesh.Add(under, p + down, VisorUv(VisorUnder, p, extent, mirror: true));
            }
        }
        for (var i = 0; i < VisorColumns; i++)
        {
            for (var k = 0; k < VisorRows; k++)
            {
                mesh.Quad(topIds[i, k], topIds[i, k + 1], topIds[i + 1, k + 1], topIds[i + 1, k]);
                mesh.Quad(underIds[i, k], underIds[i + 1, k], underIds[i + 1, k + 1], underIds[i, k + 1]);
            }
            var (u0, u1) = ((float)i / VisorColumns, (float)(i + 1) / VisorColumns);
            var a = mesh.Add(edge, points[i, VisorRows], VisorEdge.At(u0, 0));
            var b = mesh.Add(edge, points[i + 1, VisorRows], VisorEdge.At(u1, 0));
            var c = mesh.Add(edge, points[i + 1, VisorRows] + down, VisorEdge.At(u1, 1));
            var e = mesh.Add(edge, points[i, VisorRows] + down, VisorEdge.At(u0, 1));
            mesh.Quad(a, e, c, b);
        }
    }

    private static void Button(MeshBuilder mesh, Shape shape)
    {
        const int segments = 16, rings = 4;
        var radius = 0.008f * shape.Design.Size;
        var top = shape.CrownPoint(0, MathF.PI / 2, 0);
        var part = mesh.NewPart();
        var centre = new Vector2((ButtonTop.U0 + ButtonTop.U1) / 2, (ButtonTop.V0 + ButtonTop.V1) / 2);
        var uvRadius = (ButtonTop.U1 - ButtonTop.U0) / 2;
        var grid = new int[segments + 1, rings + 1];
        for (var j = 0; j <= rings; j++)
        {
            var theta = MathF.PI / 2 * j / rings;
            for (var i = 0; i <= segments; i++)
            {
                var phi = 2 * MathF.PI * i / segments;
                var spread = MathF.Cos(theta);
                var offset = new Vector3(radius * spread * MathF.Cos(phi), radius * spread * MathF.Sin(phi), 0.004f * MathF.Sin(theta) - 0.001f);
                grid[i, j] = mesh.Add(part, top + offset, centre + uvRadius * spread * new Vector2(MathF.Cos(phi), MathF.Sin(phi)));
            }
        }
        for (var i = 0; i < segments; i++)
            for (var j = 0; j < rings; j++)
                mesh.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
    }

    // Around the crown from the back (-π) through the front (0) to the back again.
    private static float Angle(int i) => -MathF.PI + 2 * MathF.PI * i / Around;

    private sealed class Shape(CapDesign design)
    {
        public CapDesign Design { get; } = design;
        private readonly float _a = HalfLength * design.Size;
        private readonly float _b = HalfWidth * design.Size;
        private readonly float _h = design.CrownHeight / 100 * design.Size;
        private readonly float _fullness = 1 - 0.55f * design.CrownShape;
        // Worn backwards the cap's own front sits low at the back of the head, so the tilt flips.
        private readonly float _tilt = (design.Backwards ? -1 : 1) * TiltDegrees * MathF.PI / 180;

        // Cap frame: X forward, Y sideways, Z up, relative to the centre of the band. The band is
        // tilted (front higher) and the tilt fades towards the top, so the crown stays upright.
        public Vector3 CrownPoint(float phi, float theta, float inset)
        {
            var spread = MathF.Pow(MathF.Max(0, MathF.Cos(theta)), _fullness);
            var forward = (_a - inset) * spread * MathF.Cos(phi);
            var up = (_h - inset) * MathF.Sin(theta) + MathF.Tan(_tilt) * forward * (1 - MathF.Sin(theta));
            return new Vector3(forward, (_b - inset) * spread * MathF.Sin(phi), up);
        }

        // Grows out of the band along its normal, dips with the visor angle (measured from level,
        // so the band's tilt is taken off) and bends down at the sides.
        public Vector3 VisorPoint(float phi, float across)
        {
            var band = CrownPoint(phi, 0, 0);
            var normal = Vector2.Normalize(new Vector2(MathF.Cos(phi) / _a, MathF.Sin(phi) / _b));
            var reach = Design.VisorLength / 100 * Design.Size * MathF.Sqrt(MathF.Max(0, MathF.Cos(MathF.PI / 2 * phi / VisorHalfAngle))) * across;
            var dip = MathF.Tan(Design.VisorAngle * MathF.PI / 180 + _tilt) * reach;
            var side = band.Y / _b;
            var bend = Design.VisorCurve * 0.045f * Design.Size * side * side * across;
            return band + new Vector3(normal.X * reach, normal.Y * reach, -dip - bend);
        }

        public Vector3 ToBone(Vector3 p)
        {
            if (Design.Backwards) p = new Vector3(-p.X, -p.Y, p.Z);
            return new Vector3(BandHeight + p.Z, HeadCentreForward + p.X, p.Y);
        }
    }
}
