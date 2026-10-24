using System.Buffers.Binary;
using System.Numerics;
using EasyClothes.Core.Model;
using EasyClothes.Core.Projects;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Modeling;

// A prop from a pack moved on the body. The untouched model is kept so every change starts from it.
public sealed record PropAdjustment(string OriginalModelPath, PropPlacement Placement);

// Moves, turns and scales the model of an existing prop (a cap from a pack, say) in place: vertices,
// normals, tangents and bounds change, while shaders, embedded textures and LODs stay as they were.
public static class PropAdjuster
{
    // Head props turn around the centre of the head, like imported models; the rest around themselves.
    public static Vector3 Pivot(PropSlot slot, MeshData original)
    {
        if (slot is PropSlot.Hat or PropSlot.Glasses or PropSlot.Ears) return ObjProp.HeadCentre;
        var (min, max) = MeshBounds.Of([original]);
        return (min + max) / 2;
    }

    public static Matrix4x4 Transform(PropPlacement p, Vector3 pivot) =>
        Matrix4x4.CreateTranslation(-pivot)
        * Matrix4x4.CreateScale(p.Scale)
        * Matrix4x4.CreateRotationX(p.Turn * MathF.PI / 180)
        * Matrix4x4.CreateRotationZ(p.Nod * MathF.PI / 180)
        * Matrix4x4.CreateRotationY(p.Tilt * MathF.PI / 180)
        * Matrix4x4.CreateTranslation(pivot + new Vector3(p.Up, p.Forward, p.Left) / 100);

    public static MeshData Place(MeshData original, PropSlot slot, PropPlacement placement) =>
        original.Transformed(Transform(placement, Pivot(slot, original)));

    // The high LOD of the model as it was before any adjustment, for previews.
    public static MeshData LoadOriginal(string projectDir, Drawable drawable) =>
        LoadMesh(Path.Combine(projectDir, drawable.Adjusted?.OriginalModelPath ?? drawable.ModelPath));

    private static MeshData LoadMesh(string path) => MeshData.Merge(DrawableDictionary.Read(ResourceFile.Read(path), includeMeshes: true)
        .SelectMany(d => d.Lods.Take(1)).SelectMany(l => l.Models).SelectMany(m => m).Select(g => g.Mesh!).ToList());

    // Returns the backup of the model it replaces.
    public static string Apply(string projectDir, Drawable drawable, PropPlacement placement)
    {
        if (drawable.Prop is not { } slot) throw new InvalidOperationException($"{drawable.Name} no es un accesorio.");
        var original = drawable.Adjusted?.OriginalModelPath;
        if (original is null)
        {
            original = Path.Combine(Path.GetDirectoryName(drawable.ModelPath) ?? "", Path.GetFileNameWithoutExtension(drawable.ModelPath) + ".original.ydd").Replace('\\', '/');
            File.Copy(Path.Combine(projectDir, drawable.ModelPath), Path.Combine(projectDir, original), overwrite: true);
        }
        var backup = ProjectBackups.Copy(projectDir, drawable.ModelPath);
        var transform = Transform(placement, Pivot(slot, LoadMesh(Path.Combine(projectDir, original))));
        var resource = ResourceFile.Read(Path.Combine(projectDir, original));
        TransformResource(resource, transform);
        resource.Write(Path.Combine(projectDir, drawable.ModelPath));
        drawable.Adjusted = new PropAdjustment(original, placement);
        return backup;
    }

    public static void TransformResource(ResourceFile resource, Matrix4x4 transform)
    {
        var reader = new ResourceReader(resource);
        Matrix4x4.Decompose(transform, out _, out var rotation, out _);
        var turn = Matrix4x4.CreateFromQuaternion(rotation);

        foreach (var drawable in reader.PointerArray(TextureDictionary.DictionaryRoot + TextureDictionary.DictionaryItems))
        {
            var (drawableMin, drawableMax) = (new Vector3(float.MaxValue), new Vector3(float.MinValue));
            var positions = new List<Vector3>();
            foreach (var lodOffset in DrawableDictionary.DrawableLodLists)
            {
                var list = reader.UInt64(drawable + lodOffset);
                if (list == 0) continue;
                foreach (var model in reader.PointerArray(list))
                {
                    var geometries = reader.PointerArray(model + DrawableDictionary.ModelGeometries);
                    var bounds = new List<(Vector3 Min, Vector3 Max)>();
                    foreach (var geometry in geometries)
                    {
                        var placed = TransformGeometry(reader, geometry, transform, turn);
                        bounds.Add(Bounds(placed));
                        positions.AddRange(placed);
                    }
                    WriteModelBounds(reader, model, bounds);
                }
            }
            if (positions.Count == 0) continue;
            foreach (var p in positions) (drawableMin, drawableMax) = (Vector3.Min(drawableMin, p), Vector3.Max(drawableMax, p));
            var centre = (drawableMin + drawableMax) / 2;
            var radius = positions.Max(p => Vector3.Distance(p, centre));
            WriteVector(reader, drawable + DrawableBoundsSphere, centre, radius);
            WriteVector(reader, drawable + DrawableBoundsMin, drawableMin, null);
            WriteVector(reader, drawable + DrawableBoundsMax, drawableMax, null);
        }
    }

    private const ulong DrawableBoundsSphere = 0x20;
    private const ulong DrawableBoundsMin = 0x30;
    private const ulong DrawableBoundsMax = 0x40;
    private const ulong ModelBounds = 0x18;

    private static List<Vector3> TransformGeometry(ResourceReader reader, ulong geometry, Matrix4x4 transform, Matrix4x4 turn)
    {
        var vb = reader.UInt64(geometry + DrawableDictionary.GeometryVertexBuffer);
        var declPtr = reader.UInt64(vb + DrawableDictionary.VertexBufferDeclaration);
        var decl = new VertexDeclaration(reader.UInt32(declPtr + DrawableDictionary.DeclarationFlags), reader.UInt64(declPtr + DrawableDictionary.DeclarationTypes));
        var count = (int)reader.UInt32(vb + DrawableDictionary.VertexBufferCount);
        var data = reader.Writable(reader.UInt64(vb + DrawableDictionary.VertexBufferData), count * decl.Stride);
        if (decl.TypeOf(VertexSemantic.Position) != VertexComponentType.Float3)
            throw new NotSupportedException("El modelo no guarda las posiciones como Float3; no se puede mover.");

        var positions = new List<Vector3>(count);
        for (var v = 0; v < count; v++)
        {
            var vertex = data.Slice(v * decl.Stride, decl.Stride);
            var p = Vector3.Transform(ReadFloat3(vertex[decl.OffsetOf(VertexSemantic.Position)..]), transform);
            WriteFloat3(vertex[decl.OffsetOf(VertexSemantic.Position)..], p);
            positions.Add(p);
            foreach (var semantic in new[] { VertexSemantic.Normal, VertexSemantic.Tangent, VertexSemantic.Binormal })
                if (decl.Has(semantic)) TurnDirection(vertex[decl.OffsetOf(semantic)..], decl.TypeOf(semantic), turn);
        }
        return positions;
    }

    // Directions only turn; the w of a tangent (its handedness) is left alone.
    private static void TurnDirection(Span<byte> at, VertexComponentType type, Matrix4x4 turn)
    {
        switch (type)
        {
            case VertexComponentType.Float3 or VertexComponentType.Float4:
                WriteFloat3(at, Vector3.TransformNormal(ReadFloat3(at), turn));
                break;
            case VertexComponentType.Dec3N:
                var packed = BinaryPrimitives.ReadUInt32LittleEndian(at);
                var turned = Vector3.TransformNormal(new Vector3(Unpack10(packed), Unpack10(packed >> 10), Unpack10(packed >> 20)), turn);
                BinaryPrimitives.WriteUInt32LittleEndian(at, (packed & 0xC0000000u) | Pack10(turned.X) | Pack10(turned.Y) << 10 | Pack10(turned.Z) << 20);
                break;
            default:
                throw new NotSupportedException($"Formato de normales {type} no admitido; no se puede mover el modelo.");
        }
    }

    private static float Unpack10(uint bits)
    {
        var value = (int)(bits & 0x3FF);
        if (value >= 512) value -= 1024;
        return Math.Max(-1, value / 511f);
    }

    private static uint Pack10(float value) => (uint)((int)MathF.Round(Math.Clamp(value, -1, 1) * 511) & 0x3FF);

    // One box per geometry plus one around all of them when there are several, as the game stores them.
    private static void WriteModelBounds(ResourceReader reader, ulong model, List<(Vector3 Min, Vector3 Max)> bounds)
    {
        var pointer = reader.UInt64(model + ModelBounds);
        if (pointer == 0 || bounds.Count == 0) return;
        if (bounds.Count > 1) bounds.Add((bounds.Select(b => b.Min).Aggregate(Vector3.Min), bounds.Select(b => b.Max).Aggregate(Vector3.Max)));
        for (var i = 0; i < bounds.Count; i++)
        {
            WriteVector(reader, pointer + (ulong)(i * 0x20), bounds[i].Min, null);
            WriteVector(reader, pointer + (ulong)(i * 0x20 + 0x10), bounds[i].Max, null);
        }
    }

    private static (Vector3 Min, Vector3 Max) Bounds(List<Vector3> points) =>
        points.Count == 0 ? (Vector3.Zero, Vector3.Zero) : (points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));

    private static void WriteVector(ResourceReader reader, ulong at, Vector3 v, float? w)
    {
        var span = reader.Writable(at, 16);
        WriteFloat3(span, v);
        if (w is { } value) BinaryPrimitives.WriteSingleLittleEndian(span[12..], value);
    }

    private static Vector3 ReadFloat3(ReadOnlySpan<byte> at) => new(
        BinaryPrimitives.ReadSingleLittleEndian(at), BinaryPrimitives.ReadSingleLittleEndian(at[4..]), BinaryPrimitives.ReadSingleLittleEndian(at[8..]));

    private static void WriteFloat3(Span<byte> at, Vector3 v)
    {
        BinaryPrimitives.WriteSingleLittleEndian(at, v.X);
        BinaryPrimitives.WriteSingleLittleEndian(at[4..], v.Y);
        BinaryPrimitives.WriteSingleLittleEndian(at[8..], v.Z);
    }
}
