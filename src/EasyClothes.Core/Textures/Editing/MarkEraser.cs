using System.Numerics;

namespace EasyClothes.Core.Textures.Editing;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public PixelRect Inflate(int by) => new(X - by, Y - by, Width + 2 * by, Height + 2 * by);

    public PixelRect ClipTo(int width, int height)
    {
        var (x0, y0) = (Math.Max(0, X), Math.Max(0, Y));
        var (x1, y1) = (Math.Min(width, Right), Math.Min(height, Bottom));
        return new PixelRect(x0, y0, Math.Max(0, x1 - x0), Math.Max(0, y1 - y0));
    }

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;
    public bool Intersects(PixelRect other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

// Where to copy the replacement from: the offset of the source area from the hole.
public readonly record struct PatchSource(int Dx, int Dy, float Cost);

// Covers a mark (logo, text, stain) with another area of the same texture: the source is the area whose
// surroundings best continue the ones around the mark, so fabric and patterns line up, and a smooth
// colour correction makes its edges meet the texture around the hole without a seam.
public static class MarkEraser
{
    private const int SearchSide = 256;
    private const int MaxRingSamples = 4000;

    // Best sources first, spread out so each one is a genuinely different area.
    public static IReadOnlyList<PatchSource> FindSources(RgbaImage image, PixelRect hole, int count = 6)
    {
        hole = hole.ClipTo(image.Width, image.Height);
        if (hole.Width == 0 || hole.Height == 0) return [];
        var ring = RingWidth(hole);

        // A coarse search over the whole texture, then each winner is refined at full size.
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(image.Width, image.Height) / (double)SearchSide));
        var small = step == 1 ? image : ImageResampler.Resize(image, Math.Max(1, image.Width / step), Math.Max(1, image.Height / step));
        var smallHole = new PixelRect(hole.X / step, hole.Y / step, Math.Max(1, (hole.Width + step - 1) / step), Math.Max(1, (hole.Height + step - 1) / step));
        var coarse = Search(small, smallHole, Math.Max(1, ring / step), stride: 1, around: null);

        var spacing = Math.Max(smallHole.Width, smallHole.Height) / 2 + 1;
        var picked = new List<PatchSource>();
        foreach (var candidate in coarse.OrderBy(c => c.Cost))
        {
            if (picked.Any(p => Math.Abs(p.Dx - candidate.Dx) < spacing && Math.Abs(p.Dy - candidate.Dy) < spacing)) continue;
            picked.Add(candidate);
            if (picked.Count == count) break;
        }
        return picked
            .Select(c => Search(image, hole, ring, stride: 1, around: (c.Dx * step, c.Dy * step, step)).MinBy(r => r.Cost))
            .OrderBy(c => c.Cost)
            .ToList();
    }

    // A layer the size of the image, transparent except over the hole.
    public static RgbaImage Fill(RgbaImage image, PixelRect hole, PatchSource source)
    {
        hole = hole.ClipTo(image.Width, image.Height);
        var layer = new byte[image.Pixels.Length];
        if (hole.Width == 0 || hole.Height == 0) return new RgbaImage(image.Width, image.Height, layer);
        var correction = Correction(image, hole, source);
        for (var y = hole.Y; y < hole.Bottom; y++)
        for (var x = hole.X; x < hole.Right; x++)
        {
            var from = Colour(image, x + source.Dx, y + source.Dy);
            var fixedColour = Vector3.Clamp(from + correction(x - hole.X, y - hole.Y), Vector3.Zero, new Vector3(255));
            var o = (y * image.Width + x) * 4;
            layer[o] = (byte)(fixedColour.X + 0.5f);
            layer[o + 1] = (byte)(fixedColour.Y + 0.5f);
            layer[o + 2] = (byte)(fixedColour.Z + 0.5f);
            layer[o + 3] = 255;
        }
        return new RgbaImage(image.Width, image.Height, layer);
    }

    private static int RingWidth(PixelRect hole) => Math.Clamp(Math.Max(hole.Width, hole.Height) / 4, 3, 32);

    // Every offset whose copied area (hole plus ring) stays inside the texture and away from the mark.
    // With `around`, only the offsets within `range` of a coarse result are tried.
    private static List<PatchSource> Search(RgbaImage image, PixelRect hole, int ring, int stride, (int Dx, int Dy, int Range)? around)
    {
        var area = hole.Inflate(ring);
        var samples = RingSamples(image, hole, area);
        var results = new List<PatchSource>();
        if (samples.Count == 0) return results;

        int minDx = -area.X, maxDx = image.Width - area.Right, minDy = -area.Y, maxDy = image.Height - area.Bottom;
        if (around is { } a)
            (minDx, maxDx, minDy, maxDy) = (Math.Max(minDx, a.Dx - a.Range), Math.Min(maxDx, a.Dx + a.Range), Math.Max(minDy, a.Dy - a.Range), Math.Min(maxDy, a.Dy + a.Range));

        var keepOut = area;
        var pixels = image.Pixels;
        var lockObject = new object();
        Parallel.For(minDy, maxDy + 1, dy =>
        {
            if ((dy - minDy) % stride != 0) return;
            var local = new List<PatchSource>();
            for (var dx = minDx; dx <= maxDx; dx += stride)
            {
                var shifted = new PixelRect(area.X + dx, area.Y + dy, area.Width, area.Height);
                if (shifted.Intersects(keepOut) || !shifted.ClipTo(image.Width, image.Height).Equals(shifted)) continue;
                if (Transparent(image, new PixelRect(hole.X + dx, hole.Y + dy, hole.Width, hole.Height))) continue;
                float cost = 0;
                foreach (var (x, y, colour) in samples)
                {
                    var o = ((y + dy) * image.Width + x + dx) * 4;
                    if (pixels[o + 3] < 128)
                    {
                        cost += 3 * 255 * 255;
                        continue;
                    }
                    var d = colour - new Vector3(pixels[o], pixels[o + 1], pixels[o + 2]);
                    cost += Vector3.Dot(d, d);
                }
                // Nearby areas usually share the lighting, so distance breaks near-ties.
                var distance = MathF.Sqrt(dx * dx + dy * dy) / Math.Max(image.Width, image.Height);
                local.Add(new PatchSource(dx, dy, cost / samples.Count * (1 + 0.1f * distance)));
            }
            lock (lockObject) results.AddRange(local);
        });
        return results;
    }

    // The visible pixels around the hole, thinned out for big marks.
    private static List<(int X, int Y, Vector3 Colour)> RingSamples(RgbaImage image, PixelRect hole, PixelRect area)
    {
        var clipped = area.ClipTo(image.Width, image.Height);
        var total = clipped.Width * clipped.Height - hole.Width * hole.Height;
        var stride = Math.Max(1, (int)Math.Sqrt(total / (double)MaxRingSamples));
        var samples = new List<(int, int, Vector3)>();
        for (var y = clipped.Y; y < clipped.Bottom; y += stride)
        for (var x = clipped.X; x < clipped.Right; x += stride)
        {
            if (hole.Contains(x, y)) continue;
            var o = (y * image.Width + x) * 4;
            if (image.Pixels[o + 3] < 128) continue;
            samples.Add((x, y, Colour(image, x, y)));
        }
        return samples;
    }

    private static bool Transparent(RgbaImage image, PixelRect rect)
    {
        var step = Math.Max(1, Math.Max(rect.Width, rect.Height) / 8);
        int seen = 0, clear = 0;
        for (var y = rect.Y; y < rect.Bottom; y += step)
        for (var x = rect.X; x < rect.Right; x += step)
        {
            seen++;
            if (image.Pixels[(y * image.Width + x) * 4 + 3] < 128) clear++;
        }
        return clear * 4 > seen;
    }

    // The difference between the texture around the hole and the copied area's own surroundings, spread
    // smoothly over the hole (a membrane fixed on its border), so the copy takes the local shading.
    private static Func<int, int, Vector3> Correction(RgbaImage image, PixelRect hole, PatchSource source)
    {
        const int MaxCells = 48;
        var cellsX = Math.Min(hole.Width + 2, MaxCells);
        var cellsY = Math.Min(hole.Height + 2, MaxCells);
        var grid = new Vector3[cellsX, cellsY];
        var weight = new float[cellsX, cellsY];
        var border = new bool[cellsX, cellsY];

        // The one-pixel frame around the hole gives the border values.
        for (var y = hole.Y - 1; y <= hole.Bottom; y++)
        for (var x = hole.X - 1; x <= hole.Right; x++)
        {
            if (hole.Contains(x, y)) continue;
            if (!InImage(image, x, y) || !InImage(image, x + source.Dx, y + source.Dy)) continue;
            var (cx, cy) = Cell(x - hole.X + 1, y - hole.Y + 1, hole.Width + 2, hole.Height + 2, cellsX, cellsY);
            grid[cx, cy] += Colour(image, x, y) - Colour(image, x + source.Dx, y + source.Dy);
            weight[cx, cy]++;
        }
        for (var cy = 0; cy < cellsY; cy++)
        for (var cx = 0; cx < cellsX; cx++)
        {
            if (cx > 0 && cy > 0 && cx < cellsX - 1 && cy < cellsY - 1) continue;
            border[cx, cy] = weight[cx, cy] > 0;
            if (border[cx, cy]) grid[cx, cy] /= weight[cx, cy];
        }

        // A selection a little too tight leaves bits of the mark on the border; those differences are far
        // larger than any shading, so they are left free instead of dragging the mark's colour inside.
        var sizes = new List<float>();
        for (var cy = 0; cy < cellsY; cy++)
        for (var cx = 0; cx < cellsX; cx++)
            if (border[cx, cy]) sizes.Add(grid[cx, cy].Length());
        if (sizes.Count > 0)
        {
            sizes.Sort();
            var limit = Math.Max(24, 3 * sizes[sizes.Count / 2]);
            var kept = 0;
            for (var cy = 0; cy < cellsY; cy++)
            for (var cx = 0; cx < cellsX; cx++)
            {
                if (!border[cx, cy]) continue;
                if (grid[cx, cy].Length() > limit) (border[cx, cy], grid[cx, cy]) = (false, Vector3.Zero);
                else kept++;
            }
            if (kept == 0) return (_, _) => Vector3.Zero;
        }

        // Gauss-Seidel with over-relaxation: a few hundred sweeps settle a 48x48 grid.
        for (var sweep = 0; sweep < 400; sweep++)
        {
            for (var cy = 0; cy < cellsY; cy++)
            for (var cx = 0; cx < cellsX; cx++)
            {
                if (border[cx, cy]) continue;
                var sum = Vector3.Zero;
                var n = 0;
                if (cx > 0) { sum += grid[cx - 1, cy]; n++; }
                if (cx < cellsX - 1) { sum += grid[cx + 1, cy]; n++; }
                if (cy > 0) { sum += grid[cx, cy - 1]; n++; }
                if (cy < cellsY - 1) { sum += grid[cx, cy + 1]; n++; }
                grid[cx, cy] += 1.8f * (sum / n - grid[cx, cy]);
            }
        }

        return (x, y) =>
        {
            var fx = (x + 1.5f) / (hole.Width + 2) * cellsX - 0.5f;
            var fy = (y + 1.5f) / (hole.Height + 2) * cellsY - 0.5f;
            var (x0, y0) = (Math.Clamp((int)MathF.Floor(fx), 0, cellsX - 1), Math.Clamp((int)MathF.Floor(fy), 0, cellsY - 1));
            var (x1, y1) = (Math.Min(x0 + 1, cellsX - 1), Math.Min(y0 + 1, cellsY - 1));
            var (tx, ty) = (Math.Clamp(fx - x0, 0, 1), Math.Clamp(fy - y0, 0, 1));
            return Vector3.Lerp(Vector3.Lerp(grid[x0, y0], grid[x1, y0], tx), Vector3.Lerp(grid[x0, y1], grid[x1, y1], tx), ty);
        };
    }

    private static (int X, int Y) Cell(int x, int y, int width, int height, int cellsX, int cellsY) =>
        (Math.Min(cellsX - 1, x * cellsX / width), Math.Min(cellsY - 1, y * cellsY / height));

    private static bool InImage(RgbaImage image, int x, int y) => x >= 0 && y >= 0 && x < image.Width && y < image.Height;

    private static Vector3 Colour(RgbaImage image, int x, int y)
    {
        var o = (y * image.Width + x) * 4;
        return new Vector3(image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2]);
    }
}
