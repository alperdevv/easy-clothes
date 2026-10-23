using System.Numerics;
using EasyClothes.Core.Textures;

namespace EasyClothes.Core.Modeling;

// Paints a generated shirt's textures from its own mesh. Each texel knows where on the body it is, so
// patterns, prints, seams, the placket and the wrinkles are worked out in 3D and line up across the UV
// seams; the fabric's fine weave is the only thing drawn in texture space.
public static class ShirtTextures
{
    public const int DiffuseSize = 1024;
    public const int NormalSize = 512;
    public const int SpecularSize = 256;

    private const float TrimWidth = 0.018f;
    private const float StitchAt = 0.022f;
    private const float RowSpacing = 0.0022f;
    private const float SideSeam = MathF.PI / 2 * 0.15f;
    private const float UnderSleeveSeam = MathF.PI * 0.045f;
    private const float PlacketHalfWidth = 0.014f;
    private const float ButtonRadius = 0.0055f;
    private const float RolledCuff = 0.035f;

    // Smaller sizes are for live previews; the saved texture uses DiffuseSize. images loads the
    // pictures printed on the shirt by their path; missing ones are skipped.
    public static RgbaImage Diffuse(ShirtDesign design, ShirtMesh shirt, int size = DiffuseSize, Func<string, RgbaImage?>? images = null)
    {
        var d = design.Clamped();
        var look = d.Look;
        var painter = new Painter(shirt, size);
        var details = new Details(d, shirt.Landmarks);
        var prints = look.Graphics.Select(g => Print.For(g, images)).OfType<Print>().ToList();
        var pixels = new byte[size * size * 4];
        var scale = 1024f / size;
        Parallel.For(0, size * size, i =>
        {
            var t = painter.At(i);
            var (x, y) = ((int)(i % size * scale), (int)(i / size * scale));
            var f = details.At(t);

            var colour = Pattern(look, shirt.Landmarks, t, Rgb(t.Sleeve > 0.5f ? look.Sleeves : look.Body));
            if (f.CuffRoll > 0) colour *= 0.97f;
            colour = Vector3.Lerp(colour, Rgb(look.Trim) * (0.94f + 0.06f * f.Rows), f.Trim);
            colour *= 1 - 0.04f * f.Placket * (1 - f.Trim);
            colour *= 1 - 0.18f * f.Seam;
            colour *= 1 - 0.08f * f.Pocket;
            foreach (var print in prints) colour = print.Over(colour, shirt.Landmarks, t);
            colour = Vector3.Lerp(colour, Rgb(look.Buttons) * (0.85f + 0.15f * f.ButtonShine), f.Button);
            colour = Vector3.Lerp(colour, Rgb(look.Stitch), 0.85f * f.Stitch);
            colour *= 1 + FabricShade(look.Fabric, x, y) + 0.1f * look.Wrinkles * f.Folds;
            colour = Wear(look, shirt.Landmarks, t, colour, x, y);
            pixels[i * 4] = ToByte(colour.X);
            pixels[i * 4 + 1] = ToByte(colour.Y);
            pixels[i * 4 + 2] = ToByte(colour.Z);
            pixels[i * 4 + 3] = 255;
        });
        return new RgbaImage(size, size, pixels);
    }

    // The fabric, seams, ribbing, buttons and wrinkles in relief. Stored in the model, so it follows the
    // look the model was last written with.
    public static RgbaImage Normal(ShirtDesign design, ShirtMesh shirt)
    {
        const int size = NormalSize;
        var d = design.Clamped();
        var look = d.Look;
        var painter = new Painter(shirt, size);
        var details = new Details(d, shirt.Landmarks);
        var height = new float[size * size];
        var scale = 1024f / size;
        Parallel.For(0, height.Length, i =>
        {
            var t = painter.At(i);
            var (x, y) = ((int)(i % size * scale), (int)(i / size * scale));
            var f = details.At(t);
            height[i] = 0.6f * f.Trim * f.Rows - 0.5f * f.Stitch - 0.6f * f.Seam + 0.3f * f.Pocket + 0.8f * f.Button + 0.4f * f.CuffRoll
                + FabricHeight(look.Fabric, x, y) + 5 * look.Wrinkles * f.Folds
                + look.Distress * 0.6f * WornSpots(t.Position, look.Distress);
        });

        var pixels = new byte[size * size * 4];
        float H(int x, int y) => height[Math.Clamp(y, 0, size - 1) * size + Math.Clamp(x, 0, size - 1)];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var n = Vector3.Normalize(new Vector3(H(x - 1, y) - H(x + 1, y), H(x, y - 1) - H(x, y + 1), 1.5f));
                var o = (y * size + x) * 4;
                pixels[o] = ToByte(n.X * 0.5f + 0.5f);
                pixels[o + 1] = ToByte(n.Y * 0.5f + 0.5f);
                pixels[o + 2] = ToByte(n.Z * 0.5f + 0.5f);
                pixels[o + 3] = 255;
            }
        }
        return new RgbaImage(size, size, pixels);
    }

    // How much light each spot reflects: set by the fabric, dulled by dirt; buttons shine.
    public static RgbaImage Specular(ShirtDesign design, ShirtMesh shirt)
    {
        const int size = SpecularSize;
        var d = design.Clamped();
        var painter = new Painter(shirt, size);
        var details = new Details(d, shirt.Landmarks);
        var shine = d.Look.Fabric switch
        {
            Fabric.Silk => 170f,
            Fabric.Leather => 120f,
            Fabric.Sport => 90f,
            Fabric.Knit => 25f,
            Fabric.Denim => 30f,
            _ => 35f,
        };
        var pixels = new byte[size * size * 4];
        Parallel.For(0, size * size, i =>
        {
            var t = painter.At(i);
            var value = shine * (1 - 0.6f * DirtMask(d.Look, d.Look.Dirt, t)) + 140 * details.At(t).Button;
            pixels[i * 4] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = (byte)Math.Clamp(value, 0, 255);
            pixels[i * 4 + 3] = 255;
        });
        return new RgbaImage(size, size, pixels);
    }

    private static Vector3 Pattern(ShirtLook look, ShirtLandmarks l, Texel t, Vector3 base_)
    {
        var p = look.Pattern;
        var colour = Rgb(p.Colour);
        var width = p.Scale / 100;
        // Soft-edged so stripes don't step along the texels.
        var soft = 0.0015f / (2 * width);
        float Band(float value)
        {
            var s = Fract(value / (2 * width));
            return SmoothStep(-soft, soft, s) * (1 - SmoothStep(0.5f - soft, 0.5f + soft, s)) + SmoothStep(1 - soft, 1 + soft, s);
        }
        switch (p.Kind)
        {
            case ShirtPatternKind.Stripes:
                return Vector3.Lerp(base_, colour, Band(t.Along));
            case ShirtPatternKind.VerticalStripes:
                return Vector3.Lerp(base_, colour, Band(t.Around));
            case ShirtPatternKind.Checks:
                return Vector3.Lerp(base_, colour, (Band(t.Along) + Band(t.Around)) / 2);
            case ShirtPatternKind.Camo:
            {
                var q = t.Position / (2 * width);
                var first = Fbm(q);
                var second = Fbm(q + new Vector3(17.3f, 5.1f, 9.7f));
                if (first > 0.56f) return colour;
                if (second > 0.58f) return Rgb(p.Colour2);
                return first < 0.36f ? base_ * 0.78f : base_;
            }
            case ShirtPatternKind.TieDye:
            {
                var q = t.Position - new Vector3(l.Axis.X, l.Axis.Y, l.Waist + 0.2f);
                var radius = new Vector2(q.X, q.Z).Length();
                var swirl = MathF.Sin(radius / width * 3 + MathF.Atan2(q.Z, q.X) * 3 + Fbm(t.Position * 8) * 4);
                return swirl < -0.2f ? Vector3.Lerp(base_, colour, SmoothStep(-0.6f, -0.2f, swirl))
                    : Vector3.Lerp(colour, Rgb(p.Colour2), SmoothStep(-0.2f, 0.6f, swirl));
            }
            case ShirtPatternKind.Gradient:
                return Vector3.Lerp(base_, colour, SmoothStep(l.Neck.Z - 0.05f, l.Waist - 0.1f, t.Position.Z));
            case ShirtPatternKind.Raglan:
                return Vector3.Lerp(base_, colour, t.Sleeve > 0.5f ? 1 : Shoulder(l, t.Position));
            case ShirtPatternKind.ColourBlock:
                return Vector3.Lerp(base_, colour, SmoothStep(l.Chest - 0.003f, l.Chest + 0.003f, t.Position.Z));
            default:
                return base_;
        }
    }

    // The raglan shoulder: above a line from the side of the neck down to the armpit.
    private static float Shoulder(ShirtLandmarks l, Vector3 p)
    {
        var side = MathF.Abs(p.X - l.Axis.X);
        var (x0, z0) = (l.NeckRadius + 0.015f, l.Neck.Z - 0.01f);
        var (x1, z1) = (MathF.Abs(l.Armpit.X - l.Axis.X), l.Armpit.Z);
        var line = z0 + (z1 - z0) * Math.Clamp((side - x0) / (x1 - x0), 0, 1);
        return side < x0 ? 0 : SmoothStep(line - 0.002f, line + 0.002f, p.Z);
    }

    private static Vector3 Wear(ShirtLook look, ShirtLandmarks l, Texel t, Vector3 colour, int x, int y)
    {
        if (look.Fade > 0)
        {
            var grey = (colour.X + colour.Y + colour.Z) / 3;
            var faded = Vector3.Lerp(new Vector3(grey), Vector3.One, 0.25f);
            colour = Vector3.Lerp(colour, faded, look.Fade * (0.55f + 0.45f * Fbm(t.Position * 20)));
        }
        if (look.Dirt > 0)
            colour = Vector3.Lerp(colour, colour * new Vector3(0.55f, 0.47f, 0.37f), 0.75f * DirtMask(look, look.Dirt, t, l.Waist));
        if (look.Distress > 0)
        {
            var frayed = (1 - SmoothStep(0, 0.004f + 0.008f * look.Distress, t.Edge)) * (Noise(x, y) > 0.5f ? 1 : 0.3f);
            colour = Vector3.Lerp(colour, Vector3.Lerp(colour, Vector3.One, 0.45f), look.Distress * frayed);
            colour = Vector3.Lerp(colour, Vector3.Lerp(colour, new Vector3(0.8f), 0.4f), WornSpots(t.Position, look.Distress));
        }
        return colour;
    }

    // Grime gathers at the openings and low on the shirt.
    private static float DirtMask(ShirtLook look, float amount, Texel t, float waist = float.NegativeInfinity)
    {
        if (amount <= 0) return 0;
        var openings = 1 - SmoothStep(0, 0.05f, t.Edge);
        var low = t.Position.Z < waist + 0.1f ? 0.2f : 0;
        return amount * Math.Clamp(Fbm(t.Position * 25) * 1.4f - 0.45f + 0.4f * openings + low, 0, 1);
    }

    private static float WornSpots(Vector3 p, float distress) =>
        distress <= 0 ? 0 : SmoothStep(1 - 0.12f * distress, 1 - 0.08f * distress, Fbm(p * 30));

    private static float FabricShade(Fabric fabric, int x, int y) => fabric switch
    {
        Fabric.Knit => 0.06f * Knit(x, y),
        Fabric.Silk => 0.015f * (Noise(x / 4, y / 4) - 0.5f),
        Fabric.Leather => -0.06f * Grain(x, y),
        Fabric.Sport => Mesh(x, y) > 0 ? -0.22f * Mesh(x, y) : 0.02f * (Noise(x, y) - 0.5f),
        Fabric.Denim => 0.08f * Twill(x, y) + 0.06f * (Noise(x, y) - 0.5f),
        _ => 0.03f * Jersey(x, y),
    };

    private static float FabricHeight(Fabric fabric, int x, int y) => fabric switch
    {
        Fabric.Knit => 0.5f * Knit(x, y),
        Fabric.Silk => 0.02f * Noise(x / 4, y / 4),
        Fabric.Leather => -0.5f * Grain(x, y),
        Fabric.Sport => -0.9f * Mesh(x, y),
        Fabric.Denim => 0.3f * Twill(x, y),
        _ => 0.15f * Jersey(x, y),
    };

    private static float Jersey(int x, int y) => MathF.Sin((x + y * 0.5f) * MathF.PI / 1.5f) * 0.5f + Noise(x, y) - 0.5f;

    // Rows of little V stitches.
    private static float Knit(int x, int y)
    {
        var v = MathF.Abs(Fract(x / 5f) - 0.5f) * 2;
        return MathF.Sin((y / 6f + v * 0.5f) * MathF.Tau);
    }

    private static float Grain(int x, int y) => SmoothStep(0.55f, 0.75f, ValueNoise2(x / 3f, y / 3f)) * 0.7f + 0.3f * Noise(x, y);

    // Round holes on a square grid, 1 inside a hole.
    private static float Mesh(int x, int y)
    {
        var dx = Fract(x / 6f) - 0.5f;
        var dy = Fract(y / 6f) - 0.5f;
        return 1 - SmoothStep(0.2f, 0.28f, MathF.Sqrt(dx * dx + dy * dy));
    }

    private static float Twill(int x, int y) => MathF.Sin((x - y) * MathF.PI / 2.5f);

    // Everything placed on the shirt's shape rather than its colour: trim, seams, stitching, the placket,
    // buttons, the pocket, rolled cuffs and wrinkles, each as a 0–1 mask (wrinkles as -1–1 height).
    private readonly record struct Feature(float Trim, float Rows, float Seam, float Stitch, float Placket, float Button, float ButtonShine,
        float Pocket, float CuffRoll, float Folds);

    private sealed class Details(ShirtDesign design, ShirtLandmarks l)
    {
        private readonly float _placketTop = l.Neck.Z - 0.032f;
        private readonly float _placketBottom = design.Collar switch
        {
            Collar.Polo => l.Neck.Z - 0.032f - 0.13f,
            Collar.Shirt => float.NegativeInfinity,
            _ => float.PositiveInfinity,
        };

        public Feature At(Texel t)
        {
            var p = t.Position;
            var sleeve = t.Sleeve > 0.5f;
            var collar = t.Collar > 0.5f;
            var front = p.Y < l.Axis.Y;
            var x = p.X - l.Axis.X;

            var cuff = design.RolledCuffs && sleeve && !collar;
            var trim = collar ? 1 : cuff ? 0 : 1 - SmoothStep(TrimWidth - 0.001f, TrimWidth + 0.001f, t.Edge);
            var rows = MathF.Sin(t.Edge / RowSpacing * MathF.Tau) * 0.5f + 0.5f;
            var stitch = Line(t.Edge, cuff ? RolledCuff : StitchAt) * (collar ? 0 : 1);
            var cuffRoll = cuff ? 1 - SmoothStep(RolledCuff - 0.002f, RolledCuff, t.Edge) : 0;

            var seam = 0f;
            if (!collar)
            {
                seam = MathF.Max(seam, 1 - SmoothStep(0, 0.05f, MathF.Abs(t.Sleeve - 0.5f)));
                seam = MathF.Max(seam, sleeve ? Thin(MathF.Abs(MathF.Abs(t.Around) - UnderSleeveSeam), 0.003f) : Thin(MathF.Abs(MathF.Abs(t.Around) - SideSeam), 0.003f));
                if (design.Look.Pattern.Kind == ShirtPatternKind.Raglan && !sleeve)
                    seam = MathF.Max(seam, 1 - SmoothStep(0, 0.6f, MathF.Abs(Shoulder(l, p) - 0.5f) * 2));
            }

            var placket = 0f;
            var button = 0f;
            var shine = 0f;
            if (front && !sleeve && !collar && p.Z < _placketTop && p.Z > _placketBottom)
            {
                placket = 1 - SmoothStep(PlacketHalfWidth - 0.0008f, PlacketHalfWidth, MathF.Abs(x));
                stitch = MathF.Max(stitch, Line(MathF.Abs(x), PlacketHalfWidth - 0.003f));
                seam = MathF.Max(seam, Thin(MathF.Abs(MathF.Abs(x) - PlacketHalfWidth), 0.0015f));
                var spacing = design.Collar == Collar.Polo ? 0.05f : 0.085f;
                var nearest = MathF.Round((_placketTop - 0.025f - p.Z) / spacing);
                if (nearest >= 0 && (design.Collar != Collar.Polo || nearest < 2))
                {
                    var centre = new Vector2(0, _placketTop - 0.025f - nearest * spacing);
                    var distance = Vector2.Distance(new Vector2(x, p.Z), centre);
                    button = 1 - SmoothStep(ButtonRadius - 0.0008f, ButtonRadius, distance);
                    shine = 1 - SmoothStep(0, ButtonRadius * 0.7f, distance);
                }
            }

            var pocket = 0f;
            if (design.Pocket && front && !sleeve && !collar)
            {
                var local = new Vector2(x - 0.085f, p.Z - (l.Chest - 0.005f));
                var inside = MathF.Max(MathF.Abs(local.X) - 0.05f, MathF.Abs(local.Y) - 0.0575f);
                if (inside < 0.001f)
                {
                    pocket = 1 - SmoothStep(-0.001f, 0.001f, inside);
                    stitch = MathF.Max(stitch, Line(inside, -0.004f));
                    seam = MathF.Max(seam, Thin(inside, 0.0012f) + Thin(local.Y - 0.045f, 0.001f) * pocket);
                }
            }

            return new Feature(trim, rows, Math.Clamp(seam, 0, 1), stitch, placket, button, shine, pocket, cuffRoll, Folds(t, collar));
        }

        // Where cloth bunches: fans from the armpits, rings round the elbows and waves above the hem.
        private float Folds(Texel t, bool collar)
        {
            if (collar) return 0;
            var p = t.Position;
            var fromArmpit = l.Mirror(p) - l.Armpit;
            var folds = 0f;
            var near = 1 - SmoothStep(0.03f, 0.15f, fromArmpit.Length());
            folds += near * MathF.Sin(MathF.Atan2(fromArmpit.Z, fromArmpit.Y) * 9 + Fbm(p * 15) * 3);
            if (t.Sleeve > 0.5f)
                folds += (1 - SmoothStep(0, 0.07f, MathF.Abs(t.Along - l.UpperArm))) * MathF.Sin(t.Around * 70 + t.Along * 45 + Fbm(p * 20) * 2);
            else
                folds += 0.7f * (1 - SmoothStep(0.02f, 0.2f, p.Z - l.Waist)) * MathF.Sin(p.Z * 90 + Fbm(p * 8) * 6);
            return 0.6f * folds + 0.3f * (Fbm(p * 12) - 0.5f) * (0.5f + design.Fit);
        }

        private static float Line(float value, float at) => 1 - SmoothStep(0, 0.0012f, MathF.Abs(value - at));

        private static float Thin(float distance, float width) => 1 - SmoothStep(0, width, MathF.Abs(distance));
    }

    // A picture or text projected onto its zone from outside the body.
    private sealed class Print
    {
        private readonly ShirtGraphic _graphic;
        private readonly RgbaImage _image;

        private Print(ShirtGraphic graphic, RgbaImage image)
        {
            _graphic = graphic;
            _image = image;
        }

        public static Print? For(ShirtGraphic graphic, Func<string, RgbaImage?>? images)
        {
            var image = graphic.Image is { Length: > 0 } path ? images?.Invoke(path)
                : graphic.Text is { Length: > 0 } text ? TextImage.Render(text, graphic.TextColour) : null;
            return image is null ? null : new Print(graphic, image);
        }

        public Vector3 Over(Vector3 colour, ShirtLandmarks l, Texel t)
        {
            if (t.Collar > 0.5f || Local(l, t) is not { } local) return colour;
            var width = _graphic.Size / 100;
            var height = width * _image.Height / _image.Width;
            var u = (local.X - _graphic.OffsetX / 100) / width + 0.5f;
            var v = 0.5f - (local.Y - _graphic.OffsetY / 100) / height;
            if (u is < 0 or >= 1 || v is < 0 or >= 1) return colour;
            var sample = Sample(u * _image.Width - 0.5f, v * _image.Height - 0.5f);
            return Vector3.Lerp(colour, new Vector3(sample.X, sample.Y, sample.Z), sample.W);
        }

        // Where the texel lies in the zone's own frame, in metres: right and up as seen from outside.
        private Vector2? Local(ShirtLandmarks l, Texel t)
        {
            var p = t.Position;
            var x = p.X - l.Axis.X;
            var front = p.Y < l.Axis.Y;
            var torso = t.Sleeve < 0.5f;
            return _graphic.Zone switch
            {
                GraphicZone.ChestCentre when torso && front => new Vector2(x, p.Z - l.Chest),
                GraphicZone.ChestLeft when torso && front => new Vector2(x - 0.085f, p.Z - (l.Chest + 0.01f)),
                GraphicZone.Belly when torso && front => new Vector2(x, p.Z - (l.Waist + 0.11f)),
                GraphicZone.Back when torso && !front => new Vector2(-x, p.Z - (l.Chest - 0.03f)),
                GraphicZone.Nape when torso && !front => new Vector2(-x, p.Z - (l.Neck.Z - 0.07f)),
                GraphicZone.Sleeve when !torso => new Vector2(t.Around, -(t.Along - l.UpperArm * 0.5f)),
                _ => null,
            };
        }

        private Vector4 Sample(float x, float y)
        {
            var (x0, y0) = ((int)MathF.Floor(x), (int)MathF.Floor(y));
            var (fx, fy) = (x - x0, y - y0);
            Vector4 Pixel(int px, int py)
            {
                px = Math.Clamp(px, 0, _image.Width - 1);
                py = Math.Clamp(py, 0, _image.Height - 1);
                var o = (py * _image.Width + px) * 4;
                return new Vector4(_image.Pixels[o], _image.Pixels[o + 1], _image.Pixels[o + 2], _image.Pixels[o + 3]) / 255;
            }
            var top = Vector4.Lerp(Pixel(x0, y0), Pixel(x0 + 1, y0), fx);
            var bottom = Vector4.Lerp(Pixel(x0, y0 + 1), Pixel(x0 + 1, y0 + 1), fx);
            return Vector4.Lerp(top, bottom, fy);
        }
    }

    private static float Fract(float x) => x - MathF.Floor(x);

    private static float Noise(int x, int y)
    {
        var h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static float Hash(int x, int y, int z)
    {
        var h = (uint)(x * 374761393 + y * 668265263 + z * 1440662683);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }

    private static float ValueNoise2(float x, float y) => ValueNoise(new Vector3(x, y, 0));

    private static float ValueNoise(Vector3 p)
    {
        var (x0, y0, z0) = ((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
        var f = p - new Vector3(x0, y0, z0);
        f = f * f * (new Vector3(3) - 2 * f);
        float Corner(int dx, int dy, int dz) => Hash(x0 + dx, y0 + dy, z0 + dz);
        var x00 = Lerp(Corner(0, 0, 0), Corner(1, 0, 0), f.X);
        var x10 = Lerp(Corner(0, 1, 0), Corner(1, 1, 0), f.X);
        var x01 = Lerp(Corner(0, 0, 1), Corner(1, 0, 1), f.X);
        var x11 = Lerp(Corner(0, 1, 1), Corner(1, 1, 1), f.X);
        return Lerp(Lerp(x00, x10, f.Y), Lerp(x01, x11, f.Y), f.Z);
    }

    // Three octaves of value noise, 0–1.
    private static float Fbm(Vector3 p) => (ValueNoise(p) * 4 + ValueNoise(p * 2.03f) * 2 + ValueNoise(p * 4.01f)) / 7;

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float from, float to, float x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Vector3 Rgb(uint rgb) => new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255;

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);

    // Along and Around are worked out per texel from the position, so they never blend arm and torso coordinates.
    private readonly record struct Texel(float Sleeve, float Edge, Vector3 Position, float Along, float Around, float Collar);

    // The shirt's paint values rasterised in UV space. Texels no triangle covers take their nearest
    // neighbour's values, so mipmaps and bilinear filtering never pull in a foreign colour.
    private sealed class Painter
    {
        private readonly int _size;
        private readonly Texel[] _texels;
        private readonly bool[] _covered;

        private readonly ShirtLandmarks _landmarks;

        public Painter(ShirtMesh shirt, int size)
        {
            _size = size;
            _landmarks = shirt.Landmarks;
            _texels = new Texel[size * size];
            Array.Fill(_texels, new Texel(0, 1, Vector3.Zero, 0, 0, 0));
            _covered = new bool[size * size];
            var mesh = shirt.Mesh;
            for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
                Fill(shirt, mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2]);
            Dilate(16);
            for (var i = 0; i < _texels.Length; i++)
            {
                var t = _texels[i];
                var sleeve = t.Sleeve > 0.5f;
                _texels[i] = t with { Along = _landmarks.Along(t.Position, sleeve), Around = _landmarks.Around(t.Position, sleeve) };
            }
        }

        public Texel At(int i) => _texels[i];

        private Vector2 Uv(ShirtMesh shirt, int v) =>
            new Vector2(shirt.Mesh.TexCoords[v * 2], shirt.Mesh.TexCoords[v * 2 + 1]) * _size;

        private void Fill(ShirtMesh shirt, int a, int b, int c)
        {
            var (pa, pb, pc) = (Uv(shirt, a), Uv(shirt, b), Uv(shirt, c));
            var area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y);
            if (MathF.Abs(area) < 1e-6f) return;
            var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.X, MathF.Min(pb.X, pc.X))));
            var maxX = Math.Min(_size - 1, (int)MathF.Ceiling(MathF.Max(pa.X, MathF.Max(pb.X, pc.X))));
            var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y))));
            var maxY = Math.Min(_size - 1, (int)MathF.Ceiling(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y))));
            var (sa, sb, sc) = (shirt.Paints[a], shirt.Paints[b], shirt.Paints[c]);
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var wa = ((pb.X - p.X) * (pc.Y - p.Y) - (pc.X - p.X) * (pb.Y - p.Y)) / area;
                    var wb = ((pc.X - p.X) * (pa.Y - p.Y) - (pa.X - p.X) * (pc.Y - p.Y)) / area;
                    var wc = 1 - wa - wb;
                    if (wa < -0.01f || wb < -0.01f || wc < -0.01f) continue;
                    var i = y * _size + x;
                    var position = wa * sa.Position + wb * sb.Position + wc * sc.Position;
                    // Both sleeves share their texture; the left one is painted, the right one shows the same.
                    if (_covered[i] && position.X <= _texels[i].Position.X) continue;
                    _texels[i] = new Texel(
                        wa * sa.Sleeve + wb * sb.Sleeve + wc * sc.Sleeve,
                        wa * sa.Edge + wb * sb.Edge + wc * sc.Edge,
                        position,
                        0,
                        0,
                        wa * sa.Collar + wb * sb.Collar + wc * sc.Collar);
                    _covered[i] = true;
                }
            }
        }

        private void Dilate(int passes)
        {
            for (var pass = 0; pass < passes; pass++)
            {
                var grown = (bool[])_covered.Clone();
                for (var y = 0; y < _size; y++)
                {
                    for (var x = 0; x < _size; x++)
                    {
                        var i = y * _size + x;
                        if (_covered[i]) continue;
                        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            var (nx, ny) = (x + dx, y + dy);
                            if (nx < 0 || ny < 0 || nx >= _size || ny >= _size || !_covered[ny * _size + nx]) continue;
                            _texels[i] = _texels[ny * _size + nx];
                            grown[i] = true;
                            break;
                        }
                    }
                }
                Array.Copy(grown, _covered, _covered.Length);
            }
        }
    }
}
