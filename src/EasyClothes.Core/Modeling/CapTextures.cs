using System.Numerics;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Modeling;

// Paints the cap's base textures on its fixed UV layout: zone colours, panel seams with stitching,
// eyelets and visor stitch rows, plus a matching normal map so the seams catch the light.
public static class CapTextures
{
    public const int DiffuseSize = 1024;
    public const int NormalSize = 512;
    public const int SpecularSize = 128;

    private const int Panels = 6;

    public static RgbaImage Diffuse(CapDesign design)
    {
        var d = design.Clamped();
        var c = d.Colours;
        var pattern = new Pattern(d, DiffuseSize);
        var pixels = new byte[DiffuseSize * DiffuseSize * 4];
        var stitch = Rgb(c.Stitch);
        for (var y = 0; y < DiffuseSize; y++)
        {
            for (var x = 0; x < DiffuseSize; x++)
            {
                var p = pattern.At(x, y);
                var colour = Base(c, x, y) * (1 - 0.45f * p.Seam) * (1 + 0.035f * p.Weave);
                colour = Vector3.Lerp(colour, stitch, 0.9f * p.Stitch);
                colour *= 1 - 0.6f * p.Eyelet;
                var o = (y * DiffuseSize + x) * 4;
                pixels[o] = ToByte(colour.X);
                pixels[o + 1] = ToByte(colour.Y);
                pixels[o + 2] = ToByte(colour.Z);
                pixels[o + 3] = 255;
            }
        }
        return new RgbaImage(DiffuseSize, DiffuseSize, pixels);
    }

    public static RgbaImage Normal(CapDesign design)
    {
        var pattern = new Pattern(design.Clamped(), NormalSize);
        var height = new float[NormalSize * NormalSize];
        for (var y = 0; y < NormalSize; y++)
        {
            for (var x = 0; x < NormalSize; x++)
            {
                var p = pattern.At(x, y);
                height[y * NormalSize + x] = -p.Seam - 0.4f * p.Stitch - 0.8f * p.Eyelet + 0.08f * p.Weave;
            }
        }

        var pixels = new byte[NormalSize * NormalSize * 4];
        float H(int x, int y) => height[Math.Clamp(y, 0, NormalSize - 1) * NormalSize + Math.Clamp(x, 0, NormalSize - 1)];
        for (var y = 0; y < NormalSize; y++)
        {
            for (var x = 0; x < NormalSize; x++)
            {
                var n = Vector3.Normalize(new Vector3(H(x - 1, y) - H(x + 1, y), H(x, y - 1) - H(x, y + 1), 1.2f));
                var o = (y * NormalSize + x) * 4;
                pixels[o] = ToByte(n.X * 0.5f + 0.5f);
                pixels[o + 1] = ToByte(n.Y * 0.5f + 0.5f);
                pixels[o + 2] = ToByte(n.Z * 0.5f + 0.5f);
                pixels[o + 3] = 255;
            }
        }
        return new RgbaImage(NormalSize, NormalSize, pixels);
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

    private static Vector3 Base(CapColours c, int x, int y)
    {
        var (u, v) = ((x + 0.5f) / DiffuseSize, (y + 0.5f) / DiffuseSize);
        bool In(UvRect r) => u >= r.U0 && u < r.U1 && v >= r.V0 && v < r.V1;
        if (In(CapModel.Crown)) return Rgb(u is >= 1f / 3 and < 2f / 3 ? c.Front : c.Crown);
        if (In(CapModel.Rim)) return Rgb(c.Crown) * 0.8f;
        if (In(CapModel.Inner)) return Rgb(c.Crown) * 0.55f;
        if (In(CapModel.VisorTop)) return Rgb(c.Visor);
        if (In(CapModel.VisorUnder)) return Rgb(c.UnderVisor);
        if (In(CapModel.VisorEdge)) return Rgb(c.Visor) * 0.85f;
        if (In(CapModel.ButtonTop)) return Rgb(c.Button);
        return Rgb(c.Crown);
    }

    private static Vector3 Rgb(uint rgb) => new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255;

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);

    private readonly record struct Detail(float Seam, float Stitch, float Eyelet, float Weave);

    // Detail masks at any texture size; distances are in pixels of a 1024 map so all sizes match.
    private sealed class Pattern
    {
        private readonly int _size;
        private readonly float _scale;
        private readonly float[] _across;
        private readonly float[] _angle;

        public Pattern(CapDesign design, int size)
        {
            _size = size;
            _scale = 1024f / size;
            (_across, _angle) = VisorMaps(design, size);
        }

        public Detail At(int x, int y)
        {
            var (u, v) = ((x + 0.5f) / _size, (y + 0.5f) / _size);
            var weave = MathF.Sin((x + y) * _scale * MathF.PI / 2) * 0.6f + Noise(x, y) * 0.4f;
            var crown = CapModel.Crown;
            if (u >= crown.U0 && u < crown.U1 && v >= crown.V0 && v < crown.V1)
                return CrownDetail(u, v, weave);

            var i = y * _size + x;
            if (!float.IsNaN(_across[i])) return VisorDetail(i, weave);
            return new Detail(0, 0, 0, weave);
        }

        private Detail CrownDetail(float u, float v, float weave)
        {
            var px = 1024f;
            var seamDistance = float.MaxValue;
            for (var k = 0; k < Panels; k++)
            {
                var d = MathF.Abs(u - Frac(0.5f + (float)k / Panels));
                seamDistance = MathF.Min(seamDistance, MathF.Min(d, 1 - d) * px);
            }
            var seam = MathF.Exp(-MathF.Pow(seamDistance / 1.6f, 2));
            var height = (v - CapModel.Crown.V0) * px;
            var bottom = (CapModel.Crown.V1 - v) * px;
            var stitch = Dash(height, 7) * Line(MathF.Abs(seamDistance - 6), 1.1f)
                         + Dash(u * px, 7) * Line(MathF.Abs(bottom - 9), 1.1f);

            // Eyelets near the top of each panel; the crown narrows there, so they are wider in U.
            var eyeletV = CapModel.Crown.V0 + 0.32f * (CapModel.Crown.V1 - CapModel.Crown.V0);
            var eyelet = 0f;
            for (var k = 0; k < Panels; k++)
            {
                var du = MathF.Abs(u - Frac(0.5f + (k + 0.5f) / Panels));
                du = MathF.Min(du, 1 - du) * px / 2.2f;
                var r = MathF.Sqrt(du * du + MathF.Pow((v - eyeletV) * px, 2));
                eyelet = MathF.Max(eyelet, r < 2.5f ? 1 : Line(MathF.Abs(r - 4), 1.3f) * 0.6f);
            }
            return new Detail(seam, Math.Clamp(stitch, 0, 1), eyelet, weave);
        }

        private Detail VisorDetail(int i, float weave)
        {
            // Rows of stitching following the visor's curve, spaced across it.
            var s = _across[i];
            var x = i % _size;
            var y = i / _size;
            var gradient = MathF.Max(MathF.Abs(At(_across, x + 1, y) - At(_across, x - 1, y)), MathF.Abs(At(_across, x, y + 1) - At(_across, x, y - 1))) / 2;
            var stitch = 0f;
            foreach (var row in (ReadOnlySpan<float>)[0.38f, 0.5f, 0.62f, 0.74f, 0.86f, 0.95f])
                stitch = MathF.Max(stitch, Line(MathF.Abs(s - row) / MathF.Max(gradient, 1e-4f), 0.9f));
            return new Detail(0, stitch * Dash(_angle[i] * 260 / _scale, 7), 0, weave * 0.5f);
        }

        private float At(float[] map, int x, int y)
        {
            var value = map[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];
            return float.IsNaN(value) ? 0 : value;
        }

        private static float Line(float distance, float halfWidth) => Math.Clamp(halfWidth + 0.5f - distance, 0, 1);

        private static float Dash(float along, float period) => Frac(along / period) < 0.6f ? 1 : 0;

        private static float Frac(float value) => value - MathF.Floor(value);

        private static float Noise(int x, int y)
        {
            var h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFF) / 32767.5f - 1;
        }
    }

    // How far across the visor (0 at the crown, 1 at the edge) and at which angle each texel of
    // the visor rectangles lies, by rasterising the visor grid in UV space.
    private static (float[] Across, float[] Angle) VisorMaps(CapDesign design, int size)
    {
        var across = new float[size * size];
        var angle = new float[size * size];
        Array.Fill(across, float.NaN);
        if (design.VisorLength <= 0) return (across, angle);

        var (points, s, phi) = CapModel.VisorGrid(design);
        var extent = CapModel.VisorExtent(points);
        foreach (var (rect, mirror) in new[] { (CapModel.VisorTop, false), (CapModel.VisorUnder, true) })
        {
            for (var i = 0; i + 1 < points.GetLength(0); i++)
            {
                for (var k = 0; k + 1 < points.GetLength(1); k++)
                {
                    var corners = new[] { (i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1) }
                        .Select(c => (Uv: CapModel.VisorUv(rect, points[c.Item1, c.Item2], extent, mirror) * size, S: s[c.Item1, c.Item2], A: phi[c.Item1, c.Item2]))
                        .ToArray();
                    Fill(across, angle, size, corners[0], corners[1], corners[2]);
                    Fill(across, angle, size, corners[0], corners[2], corners[3]);
                }
            }
        }
        return (across, angle);
    }

    private static void Fill(float[] across, float[] angle, int size, (Vector2 Uv, float S, float A) a, (Vector2 Uv, float S, float A) b, (Vector2 Uv, float S, float A) c)
    {
        var area = Cross(b.Uv - a.Uv, c.Uv - a.Uv);
        if (MathF.Abs(area) < 1e-6f) return;
        var x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Uv.X, MathF.Min(b.Uv.X, c.Uv.X))));
        var x1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Uv.X, MathF.Max(b.Uv.X, c.Uv.X))));
        var y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Uv.Y, MathF.Min(b.Uv.Y, c.Uv.Y))));
        var y1 = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(a.Uv.Y, MathF.Max(b.Uv.Y, c.Uv.Y))));
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var wa = Cross(b.Uv - p, c.Uv - p) / area;
                var wb = Cross(c.Uv - p, a.Uv - p) / area;
                var wc = 1 - wa - wb;
                if (wa < -0.01f || wb < -0.01f || wc < -0.01f) continue;
                across[y * size + x] = wa * a.S + wb * b.S + wc * c.S;
                angle[y * size + x] = wa * a.A + wb * b.A + wc * c.A;
            }
        }
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
