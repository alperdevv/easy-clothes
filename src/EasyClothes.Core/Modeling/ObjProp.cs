using System.Globalization;
using System.Numerics;
using System.Text;
using EasyClothes.Core.Importing;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Modeling;

// How an imported model sits on the head: centimetres (up, forward, to the wearer's left),
// degrees (turn, nod, tilt) and a uniform scale, all around the centre of the head.
public sealed record PropPlacement
{
    public float Up { get; init; }
    public float Forward { get; init; }
    public float Left { get; init; }
    public float Turn { get; init; }
    public float Nod { get; init; }
    public float Tilt { get; init; }
    public float Scale { get; init; } = 1;
}

// A prop made from the user's own .obj, kept in the project so it can be placed again later.
public sealed record ImportedModel(string SourcePath, PropPlacement Placement);

// Turns a user's .obj into a head prop. The .obj uses Blender's default export axes (Y up, the face
// looking along +Z, +X to the wearer's left) and metres, the same as the reference head we export.
public static class ObjProp
{
    public static readonly PropSlot[] Slots = [PropSlot.Hat, PropSlot.Glasses, PropSlot.Ears];

    internal static readonly Vector3 HeadCentre = new(0.08f, 0.012f, 0);
    // Where the lowest edge of a hat usually sits, at the back of the head.
    private const float BandBottom = 0.045f;

    // OBJ (x left, y up, z forward) to head-bone space (x up, y forward, z left).
    private static readonly Matrix4x4 ObjToBone = new(
        0, 0, 1, 0,
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 0, 1);

    public static Matrix4x4 Transform(PropPlacement p)
    {
        var rotation = Matrix4x4.CreateRotationX(p.Turn * MathF.PI / 180)
                       * Matrix4x4.CreateRotationZ(p.Nod * MathF.PI / 180)
                       * Matrix4x4.CreateRotationY(p.Tilt * MathF.PI / 180);
        return ObjToBone
               * Matrix4x4.CreateTranslation(-HeadCentre)
               * Matrix4x4.CreateScale(p.Scale)
               * rotation
               * Matrix4x4.CreateTranslation(HeadCentre + new Vector3(p.Up, p.Forward, p.Left) / 100);
    }

    // For models not made on the reference head: fixes centimetre or millimetre units and puts the
    // model's lowest edge where a hat's band sits, centred on the head.
    public static PropPlacement Fit(MeshData obj)
    {
        var bone = obj.Transformed(ObjToBone);
        var (min, max) = MeshBounds.Of([bone]);
        var width = MathF.Max(max.Z - min.Z, max.Y - min.Y);
        var scale = 1f;
        while (width * scale > 1) scale /= 10;
        while (width * scale is > 0 and < 0.05f) scale *= 10;

        // Centre on the upper half, so a visor or brim sticking out doesn't pull the crown off the head.
        var middle = (min.X + max.X) / 2;
        var upper = Enumerable.Range(0, bone.VertexCount).Where(v => bone.Positions[v * 3] >= middle)
            .Select(v => new Vector2(bone.Positions[v * 3 + 1], bone.Positions[v * 3 + 2])).ToList();
        var centre = new Vector2((upper.Min(p => p.X) + upper.Max(p => p.X)) / 2, (upper.Min(p => p.Y) + upper.Max(p => p.Y)) / 2);
        return new PropPlacement
        {
            Scale = scale,
            Up = (BandBottom - HeadCentre.X - (min.X - HeadCentre.X) * scale) * 100,
            Forward = -(centre.X - HeadCentre.Y) * scale * 100,
            Left = -centre.Y * scale * 100,
        };
    }

    public static Drawable Create(ClothingProject project, string projectDir, string objPath, string? texturePath, PropSlot slot, PedGender gender, string name, PropPlacement placement)
    {
        name = DrawableImporter.UniqueName(project, gender, name);
        var dir = DrawableImporter.UniqueDir(projectDir, Path.Combine(DrawableImporter.AssetsFolder, gender.ToString().ToLowerInvariant(), name));
        Directory.CreateDirectory(Path.Combine(projectDir, dir));
        var source = Path.Combine(dir, "modelo.obj").Replace('\\', '/');
        File.Copy(objPath, Path.Combine(projectDir, source), overwrite: true);

        var code = PedSlotNames.FileCode(slot);
        var drawable = new Drawable
        {
            Name = name,
            Gender = gender,
            Prop = slot,
            ModelPath = Path.Combine(dir, $"{code}_000.ydd").Replace('\\', '/'),
            Imported = new ImportedModel(source, placement),
        };
        WriteModel(projectDir, drawable);
        var texture = Path.Combine(dir, $"{code}_diff_000_a.ytd").Replace('\\', '/');
        WriteTexture(Path.Combine(projectDir, texture), $"{code}_diff_000_a", texturePath);
        drawable.Textures.Add(new TextureVariation { TexturePath = texture });
        DrawableOrder.PlaceAtEnd(project, drawable);
        project.Drawables.Add(drawable);
        return drawable;
    }

    // Places the stored .obj again; the textures are kept.
    public static string Replace(string projectDir, Drawable drawable, PropPlacement placement)
    {
        var imported = drawable.Imported ?? throw new InvalidOperationException($"{drawable.Name} no se importó desde un .obj.");
        var backup = ProjectBackups.Copy(projectDir, drawable.ModelPath);
        drawable.Imported = imported with { Placement = placement };
        WriteModel(projectDir, drawable);
        return backup;
    }

    public static MeshData Place(MeshData obj, PropPlacement placement) => obj.Transformed(Transform(placement));

    // The freemode head from the user's exported body files, in .obj axes, to model a prop on in Blender.
    public static void ExportReferenceHead(string bodyRoot, PedGender gender, string outputPath)
    {
        var anchor = BaseBody.PropAnchor(bodyRoot, gender, PropSlot.Hat)
            ?? throw new InvalidOperationException("Falta el esqueleto (.yft) del personaje en la carpeta del cuerpo.");
        var head = BaseBody.Parts(bodyRoot, gender, null).FirstOrDefault(p => p.Slot == ComponentSlot.Head)
            ?? throw new InvalidOperationException("No se encuentra la cabeza del personaje en la carpeta del cuerpo.");
        Matrix4x4.Invert(anchor, out var toBone);
        Matrix4x4.Invert(ObjToBone, out var boneToObj);
        var meshes = GarmentPreview.LoadPart(head.ModelPath, null, toBone * boneToObj).Meshes;

        var text = new StringBuilder("# EasyClothes: cabeza de referencia (Y arriba, mira hacia +Z, metros)\no cabeza\n");
        var offset = 1;
        foreach (var mesh in meshes)
        {
            for (var v = 0; v < mesh.VertexCount; v++)
            {
                text.Append(CultureInfo.InvariantCulture, $"v {mesh.Positions[v * 3]:0.######} {mesh.Positions[v * 3 + 1]:0.######} {mesh.Positions[v * 3 + 2]:0.######}\n");
                text.Append(CultureInfo.InvariantCulture, $"vn {mesh.Normals[v * 3]:0.####} {mesh.Normals[v * 3 + 1]:0.####} {mesh.Normals[v * 3 + 2]:0.####}\n");
            }
            for (var t = 0; t < mesh.Indices.Length; t += 3)
            {
                var (a, b, c) = (mesh.Indices[t] + offset, mesh.Indices[t + 1] + offset, mesh.Indices[t + 2] + offset);
                text.Append(CultureInfo.InvariantCulture, $"f {a}//{a} {b}//{b} {c}//{c}\n");
            }
            offset += mesh.VertexCount;
        }
        File.WriteAllText(outputPath, text.ToString());
    }

    private static void WriteModel(string projectDir, Drawable drawable)
    {
        var imported = drawable.Imported!;
        var mesh = Place(ObjReader.Read(Path.Combine(projectDir, imported.SourcePath)).Mesh, imported.Placement);
        var code = PedSlotNames.FileCode(drawable.Prop!.Value);
        var flat = new RgbaImage(64, 64, Enumerable.Range(0, 64 * 64).SelectMany(_ => new byte[] { 128, 128, 255, 255 }).ToArray());
        DrawableWriter.CreateProp(new PropDrawable(
            $"{code}_000",
            mesh,
            $"{code}_diff_000_a",
            new EmbeddedTexture($"{code}_normal_000", TextureEncoder.Encode(flat, TextureFormat.Dxt1)),
            new EmbeddedTexture($"{code}_spec_000", TextureEncoder.Encode(CapTextures.Specular(), TextureFormat.Dxt1))))
            .Write(Path.Combine(projectDir, drawable.ModelPath));
    }

    // Without an image the prop gets a plain grey texture to paint over in the editor.
    private static void WriteTexture(string path, string name, string? imagePath)
    {
        var image = imagePath is null ? new RgbaImage(512, 512, Enumerable.Repeat((byte)170, 512 * 512 * 4).ToArray()) : ImageLoader.Load(imagePath);
        var (width, height) = TextureOptimizer.TargetSize(image.Width, image.Height, 2048);
        if ((width, height) != (image.Width, image.Height)) image = ImageResampler.Resize(image, width, height);
        var format = TextureEncoder.HasTransparency(image) ? TextureFormat.Dxt5 : TextureFormat.Dxt1;
        TextureDictionaryWriter.Create([(name, TextureEncoder.Encode(image, format))]).Write(path);
    }
}
