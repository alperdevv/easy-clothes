using System.Numerics;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Modeling;

// Paints the base textures of caps and hats from their own mesh: each texel knows its zone and where
// it lies on its piece, so seams, stitching, eyelets, trucker mesh or knit follow every shape and layout.
public static class CapTextures
{
    public const int DiffuseSize = 1024;
    public const int NormalSize = 512;
    public const int SpecularSize = 128;

    private const int Panels = 6;

    // Smaller sizes are for live previews; the saved texture uses DiffuseSize.
    public static RgbaImage Diffuse(CapDesign design, int size = DiffuseSize)
    {
        var d = design.Clamped();
        var painter = new Painter(d, size);
        var stitch = Rgb(d.Colours.Stitch);
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++)
        {
            var p = painter.At(i);
            var colour = painter.Base(i) * (1 - 0.45f * p.Seam) * (1 + 0.035f * p.Weave + 0.12f * p.Knit + 0.25f * p.Fuzz);
            colour = Vector3.Lerp(colour, stitch, 0.9f * p.Stitch);
            colour *= (1 - 0.6f * p.Eyelet) * (1 - 0.75f * p.Hole);
            pixels[i * 4] = ToByte(colour.X);
            pixels[i * 4 + 1] = ToByte(colour.Y);
            pixels[i * 4 + 2] = ToByte(colour.Z);
            pixels[i * 4 + 3] = 255;
        }
        return new RgbaImage(size, size, pixels);
    }

    public static RgbaImage Normal(CapDesign design)
    {
        const int size = NormalSize;
        var painter = new Painter(design.Clamped(), size);
        var height = new float[size * size];
        for (var i = 0; i < height.Length; i++)
        {
            var p = painter.At(i);
            height[i] = -p.Seam - 0.4f * p.Stitch - 0.8f * p.Eyelet - 0.6f * p.Hole + 0.08f * p.Weave + 0.5f * p.Knit + 0.6f * p.Fuzz;
        }

        var pixels = new byte[size * size * 4];
        float H(int x, int y) => height[Math.Clamp(y, 0, size - 1) * size + Math.Clamp(x, 0, size - 1)];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var n = Vector3.Normalize(new Vector3(H(x - 1, y) - H(x + 1, y), H(x, y - 1) - H(x, y + 1), 1.2f));
                var o = (y * size + x) * 4;
                pixels[o] = ToByte(n.X * 0.5f + 0.5f);
                pixels[o + 1] = ToByte(n.Y * 0.5f + 0.5f);
                pixels[o + 2] = ToByte(n.Z * 0.5f + 0.5f);
                pixels[o + 3] = 255;
            }
        }
        return new RgbaImage(size, size, pixels);
    }

    // Fabric barely shines: a low, even specular map.
    public static RgbaImage Specular()
    {
        var pixels = new byte[SpecularSize * SpecularSize * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = pixels[i + 1] = pixels[i + 2] = 40;
            pixels[i + 3] = 255;
        }
        return new RgbaImage(SpecularSize, SpecularSize, pixels);
    }

    private static Vector3 Rgb(uint rgb) => new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255;

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);

    private readonly record struct Detail(float Seam = 0, float Stitch = 0, float Eyelet = 0, float Weave = 0, float Hole = 0, float Knit = 0, float Fuzz = 0);

    // Rasterises the paint mesh into per-texel zone / along / across maps, then turns them into detail.
    // Distances are measured in texels of a 1024 map, so every texture size draws the same thing.
    private sealed class Painter
    {
        private readonly CapDesign _design;
        private readonly int _size;
        private readonly float _scale;
        private readonly int[] _zone;
        private readonly float[] _along;
        private readonly float[] _across;

        public Painter(CapDesign design, int size)
        {
            _design = design;
            _size = size;
            _scale = 1024f / size;
            _zone = new int[size * size];
            _along = new float[size * size];
            _across = new float[size * size];
            Array.Fill(_zone, -1);
            var mesh = CapModel.Paint(design);
            for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
                Fill(mesh, mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2]);
            Dilate(4);
        }

        public Vector3 Base(int i)
        {
            var c = _design.Colours;
            var front = _along[i] is >= 1f / 3 and < 2f / 3 && _design.Kind is CapKind.Cap or CapKind.SunVisor;
            return (_zone[i] < 0 ? CapZone.Crown : (CapZone)_zone[i]) switch
            {
                CapZone.Crown or CapZone.Band => Rgb(front ? c.Front : c.Crown),
                CapZone.Rim => Rgb(c.Crown) * 0.8f,
                CapZone.Inner => Rgb(c.Crown) * 0.55f,
                CapZone.Visor => Rgb(c.Visor),
                CapZone.UnderVisor => Rgb(c.UnderVisor),
                CapZone.VisorEdge => _design.Kind == CapKind.Beanie ? Rgb(c.Front) * 0.9f : Rgb(c.Visor) * 0.85f,
                CapZone.Cuff => Rgb(c.Front),
                CapZone.Button or CapZone.Pompom => Rgb(c.Button),
                _ => Rgb(c.Crown),
            };
        }

        public Detail At(int i)
        {
            var (x, y) = (i % _size, i / _size);
            var weave = MathF.Sin((x + y) * _scale * MathF.PI / 2) * 0.6f + Noise(x, y) * 0.4f;
            if (_zone[i] < 0) return new Detail(Weave: weave);
            var knitted = _design.Kind == CapKind.Beanie;
            return (CapZone)_zone[i] switch
            {
                CapZone.Crown when knitted => Knit(i, x, y),
                CapZone.Crown => Crown(i, x, y, weave),
                CapZone.Cuff => Knit(i, x, y),
                CapZone.Visor => Rows(i, weave, _design.Kind == CapKind.Bucket ? [0.2f, 0.35f, 0.5f, 0.65f, 0.8f, 0.93f] : [0.38f, 0.5f, 0.62f, 0.74f, 0.86f, 0.95f]),
                CapZone.Band => Rows(i, weave, [0.15f, 0.85f]),
                CapZone.Pompom => new Detail(Fuzz: Noise(x, y) * 0.7f + Noise(x / 3, y / 3) * 0.5f),
                _ => new Detail(Weave: knitted ? 0 : weave),
            };
        }

        private Detail Crown(int i, int x, int y, float weave)
        {
            var (along, across) = (_along[i], _across[i]);
            var (gAlong, gAcross) = Gradients(i);
            var seamDistance = float.MaxValue;
            for (var k = 0; k < Panels; k++)
                seamDistance = MathF.Min(seamDistance, MathF.Abs(Wrap(along - Frac(0.5f + (float)k / Panels))) / gAlong);
            var fromBand = across / gAcross;
            var seam = MathF.Exp(-MathF.Pow(seamDistance / 1.6f, 2));
            var stitch = Dash(fromBand, 7) * Line(MathF.Abs(seamDistance - 6), 1.1f) + Dash(along / gAlong, 7) * Line(MathF.Abs(fromBand - 9), 1.1f);

            var eyelet = 0f;
            if (_design.Kind == CapKind.Cap)
            {
                for (var k = 0; k < Panels; k++)
                {
                    var dx = Wrap(along - Frac(0.5f + (k + 0.5f) / Panels)) / gAlong;
                    var r = MathF.Sqrt(dx * dx + MathF.Pow((across - 0.68f) / gAcross, 2));
                    eyelet = MathF.Max(eyelet, r < 2.5f ? 1 : Line(MathF.Abs(r - 4), 1.3f) * 0.6f);
                }
            }
            var hole = _design.Style == CapStyle.Trucker && along is < 1f / 3 or >= 2f / 3 && fromBand > 14 ? MeshHole(x * _scale, y * _scale) : 0;
            return new Detail(seam, Math.Clamp(stitch, 0, 1), hole > 0 ? 0 : eyelet, weave, hole);
        }

        // Rows of stitching following the piece's edge, dashed along it.
        private Detail Rows(int i, float weave, ReadOnlySpan<float> rows)
        {
            var (gAlong, gAcross) = Gradients(i);
            var stitch = 0f;
            foreach (var row in rows)
                stitch = MathF.Max(stitch, Line(MathF.Abs(_across[i] - row) / gAcross, 0.9f));
            return new Detail(Stitch: stitch * Dash(_along[i] / gAlong, 7), Weave: weave * 0.5f);
        }

        // Ribbed knit: ribs run up the hat with a small V stitch inside each one.
        private Detail Knit(int i, int x, int y)
        {
            var (gAlong, _) = Gradients(i);
            var rib = _along[i] / gAlong / 7;
            var v = MathF.Abs(Frac(rib) - 0.5f) * 2;
            var stitch = MathF.Sin((y * _scale / 4 + v * 2) * MathF.PI);
            return new Detail(Knit: 0.6f * MathF.Cos(rib * 2 * MathF.PI) + 0.4f * stitch, Weave: Noise(x, y) * 0.3f);
        }

        // Change of along/across per texel (in 1024-map texels), wrapping round at the back of the crown.
        private (float Along, float Across) Gradients(int i)
        {
            var (x, y) = (i % _size, i / _size);
            float Diff(float[] map, int dx, int dy, bool wrap)
            {
                var (x0, y0, x1, y1) = (Math.Max(0, x - dx), Math.Max(0, y - dy), Math.Min(_size - 1, x + dx), Math.Min(_size - 1, y + dy));
                var (a, b) = (y0 * _size + x0, y1 * _size + x1);
                if (_zone[a] != _zone[i]) a = i;
                if (_zone[b] != _zone[i]) b = i;
                var d = map[b] - map[a];
                return MathF.Abs(wrap ? Wrap(d) : d) / Math.Max(1, (x1 - x0) + (y1 - y0));
            }
            var along = MathF.Max(Diff(_along, 1, 0, true), Diff(_along, 0, 1, true));
            var across = MathF.Max(Diff(_across, 1, 0, false), Diff(_across, 0, 1, false));
            return (MathF.Max(along, 1e-5f) / _scale, MathF.Max(across, 1e-5f) / _scale);
        }

        private void Fill(PaintMesh mesh, int ia, int ib, int ic)
        {
            var (a, b, c) = (mesh.Uvs[ia] * _size, mesh.Uvs[ib] * _size, mesh.Uvs[ic] * _size);
            var area = Cross(b - a, c - a);
            if (MathF.Abs(area) < 1e-6f) return;
            var (pa, pb, pc) = (mesh.Paints[ia], mesh.Paints[ib], mesh.Paints[ic]);
            var x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
            var x1 = Math.Min(_size - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
            var y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
            var y1 = Math.Min(_size - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var wa = Cross(b - p, c - p) / area;
                    var wb = Cross(c - p, a - p) / area;
                    var wc = 1 - wa - wb;
                    if (wa < -0.01f || wb < -0.01f || wc < -0.01f) continue;
                    var i = y * _size + x;
                    _zone[i] = (int)pa.Zone;
                    _along[i] = wa * pa.Along + wb * pb.Along + wc * pc.Along;
                    _across[i] = wa * pa.Across + wb * pb.Across + wc * pc.Across;
                }
            }
        }

        // Grows each piece a few texels into the empty space around it, so mipmaps don't bleed other colours in.
        private void Dilate(int passes)
        {
            for (var pass = 0; pass < passes; pass++)
            {
                var zone = (int[])_zone.Clone();
                for (var i = 0; i < zone.Length; i++)
                {
                    if (zone[i] >= 0) continue;
                    var (x, y) = (i % _size, i / _size);
                    foreach (var n in (ReadOnlySpan<int>)[x > 0 ? i - 1 : -1, x < _size - 1 ? i + 1 : -1, y > 0 ? i - _size : -1, y < _size - 1 ? i + _size : -1])
                    {
                        if (n < 0 || zone[n] < 0) continue;
                        _zone[i] = zone[n];
                        _along[i] = _along[n];
                        _across[i] = _across[n];
                        break;
                    }
                }
            }
        }

        // Trucker back panels: staggered diamond holes in a plastic mesh.
        private static float MeshHole(float x, float y)
        {
            const float cell = 9;
            var column = MathF.Floor(x / cell);
            var dx = Frac(x / cell) - 0.5f;
            var dy = Frac(y / cell + (column % 2) * 0.5f) - 0.5f;
            return Math.Clamp((0.32f - MathF.Abs(dx) - MathF.Abs(dy)) * 8, 0, 1);
        }

        private static float Line(float distance, float halfWidth) => Math.Clamp(halfWidth + 0.5f - distance, 0, 1);

        private static float Dash(float along, float period) => Frac(along / period) < 0.6f ? 1 : 0;

        private static float Frac(float value) => value - MathF.Floor(value);

        private static float Wrap(float delta) => delta - MathF.Round(delta);

        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

        private static float Noise(int x, int y)
        {
            var h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 32767.5f - 1;
        }
    }
}
