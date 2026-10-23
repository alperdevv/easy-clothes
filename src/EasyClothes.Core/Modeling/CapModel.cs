using System.Numerics;
using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Modeling;

public readonly record struct UvRect(float U0, float V0, float U1, float V1)
{
    public Vector2 At(float u, float v) => new(U0 + u * (U1 - U0), V0 + v * (V1 - V0));
}

// Where each piece goes in the texture. Fixed per kind, so painted textures survive any change of shape.
// Extra is the button on caps and the pompom on beanies.
public sealed record CapLayout(UvRect Crown, UvRect Rim, UvRect Inner, UvRect Visor, UvRect UnderVisor, UvRect Edge, UvRect Extra)
{
    public static CapLayout For(CapKind kind) => kind switch
    {
        CapKind.Bucket => new(new(0, 0, 1, 0.25f), new(0, 0.25f, 1, 0.27f), new(0, 0.62f, 1, 0.87f), new(0, 0.30f, 1, 0.42f), new(0, 0.44f, 1, 0.56f), new(0, 0.58f, 1, 0.60f), default),
        CapKind.Beanie => new(new(0, 0, 1, 0.25f), new(0, 0.25f, 1, 0.27f), new(0, 0.62f, 1, 0.87f), new(0, 0.30f, 1, 0.40f), default, new(0, 0.42f, 1, 0.44f), new(0, 0.46f, 0.14f, 0.60f)),
        CapKind.SunVisor => new(new(0, 0, 1, 0.08f), new(0, 0.10f, 1, 0.12f), new(0, 0.14f, 1, 0.22f), new(0, 0.30f, 0.5f, 0.50f), new(0.5f, 0.30f, 1, 0.50f), new(0, 0.52f, 1, 0.54f), default),
        _ => new(new(0, 0, 1, 0.25f), new(0, 0.25f, 1, 0.27f), new(0, 0.62f, 1, 0.87f), new(0, 0.30f, 0.5f, 0.50f), new(0.5f, 0.30f, 1, 0.50f), new(0, 0.52f, 1, 0.54f), new(0, 0.56f, 0.05f, 0.61f)),
    };
}

// Builds caps and hats in head-bone space (+X up, +Y towards the face, +Z to the wearer's left), fitted
// to the freemode head. Shapes are worked out in a head frame (forward, left, up) and placed at the end.
// Turning moves the panels and visor round the head; the crown keeps the head's shape, like fabric.
public static class CapModel
{
    // Fit to the freemode head (measured on the game's head in bone space), in metres.
    private const float HeadCentreForward = 0.012f;
    private const float HalfLength = 0.112f;
    private const float HalfWidth = 0.096f;
    // The skull as an ellipsoid (bone space centre; radii up, forward, sideways), fitted to the same head.
    private static readonly Vector3 SkullCentre = new(0.075f, 0.016f, 0);
    private static readonly Vector3 SkullRadii = new(0.101f, 0.104f, 0.092f);
    private const float Thickness = 0.003f;
    private const float VisorThickness = 0.004f;

    private const int Around = 48;
    private const int Rings = 14;
    private const int VisorColumns = 40;
    private const int VisorRows = 8;

    public static MeshData Build(CapDesign design) => Generate(design).Mesh;

    public static PaintMesh Paint(CapDesign design) => Generate(design).Paint;

    private static (MeshData Mesh, PaintMesh Paint) Generate(CapDesign design)
    {
        var d = design.Clamped();
        var shape = new Shape(d);
        var layout = CapLayout.For(d.Kind);
        var mesh = new MeshBuilder();
        switch (d.Kind)
        {
            case CapKind.SunVisor:
                SunVisorBand(mesh, shape, layout);
                Visor(mesh, shape, layout);
                break;
            case CapKind.Beanie:
                Beanie(mesh, shape, layout);
                break;
            default:
                var outer = CrownGrid(mesh, shape, 0, layout.Crown, outward: true);
                var inner = CrownGrid(mesh, shape, Thickness, layout.Inner, outward: false);
                Strip(mesh, outer, inner, layout.Rim, CapZone.Rim);
                if (d.Kind == CapKind.Bucket) Brim(mesh, shape, layout);
                else if (d.VisorLength > 0) Visor(mesh, shape, layout);
                if (d.Kind == CapKind.Cap && d.Button) Button(mesh, shape, layout.Extra);
                break;
        }
        return (mesh.ToMesh(shape.ToBone), mesh.ToPaintMesh());
    }

    // Rings of the crown from the band (j = 0) towards the top, closed at the pole. Returns the band ring.
    private static int[] CrownGrid(MeshBuilder mesh, Shape shape, float inset, UvRect rect, bool outward)
    {
        var part = mesh.NewPart();
        var zone = outward ? CapZone.Crown : CapZone.Inner;
        var grid = new int[Around + 1, Rings];
        for (var j = 0; j < Rings; j++)
        {
            var theta = MathF.PI / 2 * j / Rings;
            for (var i = 0; i <= Around; i++)
            {
                var along = (float)i / Around;
                grid[i, j] = mesh.Add(part, shape.CrownPoint(Angle(i), theta, inset), rect.At(along, 1 - (float)j / Rings), new(zone, along, (float)j / Rings));
            }
        }
        for (var i = 0; i < Around; i++)
        {
            for (var j = 0; j + 1 < Rings; j++)
            {
                if (outward) mesh.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                else mesh.Quad(grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j]);
            }
            var along = (i + 0.5f) / Around;
            var pole = mesh.Add(part, shape.CrownPoint(0, MathF.PI / 2, inset), rect.At(along, 0), new(zone, along, 1));
            if (outward) mesh.Triangle(grid[i, Rings - 1], grid[i + 1, Rings - 1], pole);
            else mesh.Triangle(grid[i, Rings - 1], pole, grid[i + 1, Rings - 1]);
        }
        return Enumerable.Range(0, Around + 1).Select(i => grid[i, 0]).ToArray();
    }

    // Joins two matching rings (an outer and an inner edge) with a strip facing down, or up.
    private static void Strip(MeshBuilder mesh, int[] outer, int[] inner, UvRect rect, CapZone zone, bool up = false)
    {
        var part = mesh.NewPart();
        for (var i = 0; i + 1 < outer.Length; i++)
        {
            var (u0, u1) = ((float)i / (outer.Length - 1), (float)(i + 1) / (outer.Length - 1));
            var a = mesh.Add(part, mesh.PositionOf(outer[i]), rect.At(u0, 0), new(zone, u0, 0));
            var b = mesh.Add(part, mesh.PositionOf(outer[i + 1]), rect.At(u1, 0), new(zone, u1, 0));
            var c = mesh.Add(part, mesh.PositionOf(inner[i + 1]), rect.At(u1, 1), new(zone, u1, 1));
            var e = mesh.Add(part, mesh.PositionOf(inner[i]), rect.At(u0, 1), new(zone, u0, 1));
            if (up) mesh.Quad(a, b, c, e);
            else mesh.Quad(a, e, c, b);
        }
    }

    // A front visor: top and underside mapped as seen from above (front edge at the top of its
    // rectangle, the underside mirrored so it reads right from below), plus the thin edge.
    private static void Visor(MeshBuilder mesh, Shape shape, CapLayout layout)
    {
        var points = new Vector3[VisorColumns + 1, VisorRows + 1];
        var flat = new Vector3[VisorColumns + 1, VisorRows + 1];
        for (var i = 0; i <= VisorColumns; i++)
        {
            var phi = -shape.VisorHalfAngle + 2 * shape.VisorHalfAngle * i / VisorColumns;
            for (var k = 0; k <= VisorRows; k++)
            {
                points[i, k] = shape.VisorPoint(phi, (float)k / VisorRows);
                flat[i, k] = shape.ToCapFrame(points[i, k]);
            }
        }
        var all = flat.Cast<Vector3>().ToList();
        var (side, back, front) = (all.Max(p => MathF.Abs(p.Y)), all.Min(p => p.X), all.Max(p => p.X));
        Vector2 Uv(UvRect rect, Vector3 p, bool mirror) =>
            rect.At(mirror ? 0.5f - 0.5f * p.Y / side : 0.5f + 0.5f * p.Y / side, (front - p.X) / (front - back));

        var down = new Vector3(0, 0, -VisorThickness);
        var (top, under, edge) = (mesh.NewPart(), mesh.NewPart(), mesh.NewPart());
        var topIds = new int[VisorColumns + 1, VisorRows + 1];
        var underIds = new int[VisorColumns + 1, VisorRows + 1];
        for (var i = 0; i <= VisorColumns; i++)
        {
            var along = (float)i / VisorColumns;
            for (var k = 0; k <= VisorRows; k++)
            {
                var across = (float)k / VisorRows;
                topIds[i, k] = mesh.Add(top, points[i, k], Uv(layout.Visor, flat[i, k], false), new(CapZone.Visor, along, across));
                underIds[i, k] = mesh.Add(under, points[i, k] + down, Uv(layout.UnderVisor, flat[i, k], true), new(CapZone.UnderVisor, along, across));
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
            var a = mesh.Add(edge, points[i, VisorRows], layout.Edge.At(u0, 0), new(CapZone.VisorEdge, u0, 0));
            var b = mesh.Add(edge, points[i + 1, VisorRows], layout.Edge.At(u1, 0), new(CapZone.VisorEdge, u1, 0));
            var c = mesh.Add(edge, points[i + 1, VisorRows] + down, layout.Edge.At(u1, 1), new(CapZone.VisorEdge, u1, 1));
            var e = mesh.Add(edge, points[i, VisorRows] + down, layout.Edge.At(u0, 1), new(CapZone.VisorEdge, u0, 1));
            mesh.Quad(a, e, c, b);
        }
    }

    // The bucket hat's brim goes all the way round, sloping down; mapped as a band (round the hat, outwards).
    private static void Brim(MeshBuilder mesh, Shape shape, CapLayout layout)
    {
        const int rows = 6;
        var down = new Vector3(0, 0, -Thickness);
        var (top, under) = (mesh.NewPart(), mesh.NewPart());
        var topIds = new int[Around + 1, rows + 1];
        var underIds = new int[Around + 1, rows + 1];
        for (var i = 0; i <= Around; i++)
        {
            var along = (float)i / Around;
            for (var k = 0; k <= rows; k++)
            {
                var across = (float)k / rows;
                var p = shape.BrimPoint(Angle(i), across);
                topIds[i, k] = mesh.Add(top, p, layout.Visor.At(along, across), new(CapZone.Visor, along, across));
                underIds[i, k] = mesh.Add(under, p + down, layout.UnderVisor.At(along, across), new(CapZone.UnderVisor, along, across));
            }
        }
        for (var i = 0; i < Around; i++)
        {
            for (var k = 0; k < rows; k++)
            {
                mesh.Quad(topIds[i, k], topIds[i, k + 1], topIds[i + 1, k + 1], topIds[i + 1, k]);
                mesh.Quad(underIds[i, k], underIds[i + 1, k], underIds[i + 1, k + 1], underIds[i, k + 1]);
            }
        }
        var outerTop = Enumerable.Range(0, Around + 1).Select(i => topIds[i, rows]).ToArray();
        var outerUnder = Enumerable.Range(0, Around + 1).Select(i => underIds[i, rows]).ToArray();
        Strip(mesh, outerTop, outerUnder, layout.Edge, CapZone.VisorEdge, up: true);
    }

    // Knitted crown with a folded cuff round the bottom and an optional pompom on top.
    private static void Beanie(MeshBuilder mesh, Shape shape, CapLayout layout)
    {
        const int cuffRings = 4;
        var outer = CrownGrid(mesh, shape, 0, layout.Crown, outward: true);
        var inner = CrownGrid(mesh, shape, Thickness, layout.Inner, outward: false);
        var cuffHeight = shape.Design.CuffHeight / 100 * shape.Design.Size;
        if (cuffHeight <= 0)
        {
            Strip(mesh, outer, inner, layout.Rim, CapZone.Rim);
        }
        else
        {
            var part = mesh.NewPart();
            var cuff = new int[Around + 1, cuffRings + 1];
            for (var j = 0; j <= cuffRings; j++)
            {
                var across = (float)j / cuffRings;
                for (var i = 0; i <= Around; i++)
                {
                    var along = (float)i / Around;
                    cuff[i, j] = mesh.Add(part, shape.CuffPoint(Angle(i), cuffHeight * across), layout.Visor.At(along, 1 - across), new(CapZone.Cuff, along, across));
                }
            }
            for (var i = 0; i < Around; i++)
                for (var j = 0; j < cuffRings; j++)
                    mesh.Quad(cuff[i, j], cuff[i + 1, j], cuff[i + 1, j + 1], cuff[i, j + 1]);

            var cuffBottom = Enumerable.Range(0, Around + 1).Select(i => cuff[i, 0]).ToArray();
            var cuffTop = Enumerable.Range(0, Around + 1).Select(i => cuff[i, cuffRings]).ToArray();
            var fold = mesh.NewPart();
            var crownAtFold = Enumerable.Range(0, Around + 1)
                .Select(i => mesh.Add(fold, shape.CrownPoint(Angle(i), shape.ThetaAtHeight(cuffHeight), 0), default, new(CapZone.VisorEdge, (float)i / Around, 1)))
                .ToArray();
            Strip(mesh, cuffBottom, inner, layout.Rim, CapZone.Rim);
            Strip(mesh, crownAtFold, cuffTop, layout.Edge, CapZone.VisorEdge, up: true);
        }
        if (shape.Design.Pompom) Pompom(mesh, shape, layout.Extra);
    }

    // A band round the head with a visor at the front and no crown.
    private static void SunVisorBand(MeshBuilder mesh, Shape shape, CapLayout layout)
    {
        const float height = 0.035f;
        var (outerPart, innerPart) = (mesh.NewPart(), mesh.NewPart());
        int[] Ring(int part, float inset, float lift, UvRect rect, CapZone zone) => Enumerable.Range(0, Around + 1)
            .Select(i => mesh.Add(part, shape.CrownPoint(Angle(i), 0, inset) + new Vector3(0, 0, lift), rect.At((float)i / Around, 1 - lift / height), new(zone, (float)i / Around, lift / height)))
            .ToArray();
        var outerBottom = Ring(outerPart, 0, 0, layout.Crown, CapZone.Band);
        var outerTop = Ring(outerPart, 0, height, layout.Crown, CapZone.Band);
        var innerBottom = Ring(innerPart, Thickness, 0, layout.Inner, CapZone.Inner);
        var innerTop = Ring(innerPart, Thickness, height, layout.Inner, CapZone.Inner);
        for (var i = 0; i < Around; i++)
        {
            mesh.Quad(outerBottom[i], outerBottom[i + 1], outerTop[i + 1], outerTop[i]);
            mesh.Quad(innerBottom[i], innerTop[i], innerTop[i + 1], innerBottom[i + 1]);
        }
        Strip(mesh, outerBottom, innerBottom, layout.Rim, CapZone.Rim);
        Strip(mesh, outerTop, innerTop, layout.Rim, CapZone.Rim, up: true);
    }

    private static void Button(MeshBuilder mesh, Shape shape, UvRect rect) =>
        Dome(mesh, shape.CrownPoint(0, MathF.PI / 2, 0) + new Vector3(0, 0, -0.001f), 0.008f * shape.Design.Size, 0.004f, rect, CapZone.Button);

    private static void Pompom(MeshBuilder mesh, Shape shape, UvRect rect)
    {
        var radius = 0.035f * shape.Design.Size;
        Dome(mesh, shape.CrownPoint(0, MathF.PI / 2, 0) + new Vector3(0, 0, radius * 0.8f), radius, radius, rect, CapZone.Pompom, sphere: true);
    }

    // A dome (or a whole ball) mapped as a disc seen from above.
    private static void Dome(MeshBuilder mesh, Vector3 centre, float radius, float height, UvRect rect, CapZone zone, bool sphere = false)
    {
        const int segments = 16, rings = 8;
        var part = mesh.NewPart();
        var uvCentre = new Vector2((rect.U0 + rect.U1) / 2, (rect.V0 + rect.V1) / 2);
        var uvRadius = (rect.U1 - rect.U0) / 2;
        var from = sphere ? -rings : 0;
        var grid = new int[segments + 1, rings - from + 1];
        for (var j = from; j <= rings; j++)
        {
            var theta = MathF.PI / 2 * j / rings;
            var spread = MathF.Cos(theta);
            var disc = sphere ? 1 - (float)(j - from) / (rings - from) : spread;
            for (var i = 0; i <= segments; i++)
            {
                var phi = 2 * MathF.PI * i / segments;
                var offset = new Vector3(radius * spread * MathF.Cos(phi), radius * spread * MathF.Sin(phi), height * MathF.Sin(theta));
                grid[i, j - from] = mesh.Add(part, centre + offset, uvCentre + uvRadius * disc * new Vector2(MathF.Cos(phi), MathF.Sin(phi)),
                    new(zone, (float)i / segments, (float)(j - from) / (rings - from)));
            }
        }
        for (var i = 0; i < segments; i++)
            for (var j = 0; j < rings - from; j++)
                mesh.Quad(grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
    }

    // Round the crown from the back (-π) through the front (0) to the back again.
    private static float Angle(int i) => -MathF.PI + 2 * MathF.PI * i / Around;

    private sealed class Shape
    {
        public CapDesign Design { get; }
        public float VisorHalfAngle { get; }
        private readonly float _a, _b, _h, _band, _tilt, _fullness, _turn, _taper;

        public Shape(CapDesign design)
        {
            Design = design;
            // Bucket hats and beanies sit lower and looser than caps.
            var (band, tilt, slack) = design.Kind switch
            {
                CapKind.Bucket => (0.072f, 12f, 0.004f),
                CapKind.Beanie => (0.058f, 10f, 0.002f),
                CapKind.SunVisor => (0.076f, 16f, 0f),
                _ => (0.08f, 18f, 0f),
            };
            _band = band;
            _tilt = MathF.Tan(tilt * MathF.PI / 180);
            _a = (HalfLength + slack) * design.Size;
            _b = (HalfWidth + slack) * design.Size;
            _h = design.CrownHeight / 100 * design.Size;
            _fullness = 1 - 0.55f * design.CrownShape;
            _turn = design.Rotation * MathF.PI / 180;
            _taper = 0.5f - 0.38f * design.VisorSquare;
            VisorHalfAngle = (48 + 24 * design.VisorWidth) * MathF.PI / 180;
        }

        // phi is measured round the cap from its front. The band is tilted (forehead higher) and the
        // tilt fades towards the top, so the crown stays upright.
        public Vector3 CrownPoint(float phi, float theta, float inset)
        {
            var spread = MathF.Pow(MathF.Max(0, MathF.Cos(theta)), _fullness);
            var forward = (_a - inset) * spread * MathF.Cos(phi + _turn);
            var up = (_h - inset) * MathF.Sin(theta) + _tilt * forward * (1 - MathF.Sin(theta));
            return ClearOfSkull(new Vector3(forward, (_b - inset) * spread * MathF.Sin(phi + _turn), up), inset);
        }

        // Whatever the settings, fabric is pushed out to sit on the skull, never inside it.
        private Vector3 ClearOfSkull(Vector3 p, float inset)
        {
            var centre = new Vector3(SkullCentre.Y - HeadCentreForward, SkullCentre.Z, SkullCentre.X - _band);
            var radii = new Vector3(SkullRadii.Y, SkullRadii.Z, SkullRadii.X);
            var offset = p - centre;
            var reach = (offset / radii).Length();
            var least = 1.05f - inset * 10;
            return reach >= least || reach < 1e-6f ? p : centre + offset * (least / reach);
        }

        public float ThetaAtHeight(float height) => MathF.Asin(Math.Clamp(height / _h, 0, 0.95f));

        // The folded cuff stands just outside the crown.
        public Vector3 CuffPoint(float phi, float height) => CrownPoint(phi, ThetaAtHeight(height), -0.004f);

        // Grows out of the band along its normal, dips with the visor angle and bends down at the sides.
        public Vector3 VisorPoint(float phi, float across)
        {
            var profile = MathF.Pow(MathF.Max(0, MathF.Cos(MathF.PI / 2 * phi / VisorHalfAngle)), _taper);
            var reach = Design.VisorLength / 100 * Design.Size * profile * across;
            var side = MathF.Sin(phi);
            var bend = Design.VisorCurve * 0.045f * Design.Size * side * side * across;
            return Out(phi, reach, MathF.Tan(Design.VisorAngle * MathF.PI / 180) * reach + bend);
        }

        // The brim droops more towards its edge, like soft cotton.
        public Vector3 BrimPoint(float phi, float across)
        {
            var reach = Design.BrimWidth / 100 * Design.Size * across;
            return Out(phi, reach, MathF.Tan(Design.BrimDroop * MathF.PI / 180) * reach * MathF.Sqrt(across));
        }

        // Undoes the turn, so the visor's texture layout is the same whichever way the cap faces.
        public Vector3 ToCapFrame(Vector3 p)
        {
            var (cos, sin) = (MathF.Cos(_turn), MathF.Sin(_turn));
            return new Vector3(p.X * cos + p.Y * sin, -p.X * sin + p.Y * cos, p.Z);
        }

        public Vector3 ToBone(Vector3 p) => new(_band + p.Z, HeadCentreForward + p.X, p.Y);

        private Vector3 Out(float phi, float reach, float drop)
        {
            var head = phi + _turn;
            var normal = Vector2.Normalize(new Vector2(MathF.Cos(head) / _a, MathF.Sin(head) / _b));
            return CrownPoint(phi, 0, 0) + new Vector3(normal.X * reach, normal.Y * reach, -drop);
        }
    }
}
