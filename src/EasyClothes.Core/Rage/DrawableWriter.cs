using System.Buffers.Binary;
using System.Numerics;
using EasyClothes.Core.Meshes;
using EasyClothes.Core.Textures;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Rage;

public sealed record EmbeddedTexture(string Name, EncodedTexture Texture);

// A rigid prop: one mesh in its anchor bone's space, drawn with the ped shader. The diffuse map
// lives in the .ytd next to it; the normal and specular maps are embedded like the game's own props.
public sealed record PropDrawable(string Name, MeshData Mesh, string DiffuseName, EmbeddedTexture Normal, EmbeddedTexture Specular);

// Writes drawable dictionaries (.ydd) with the structures and values found in the game's ped files.
public static class DrawableWriter
{
    // Virtual table ids as the game stores them; it replaces them on load.
    private const ulong DictionaryVft = 0x1_40571578;
    private const ulong DrawableVft = 0x1_40571168;
    private const ulong ShaderGroupVft = 0x1_406117F0;
    private const ulong TextureDictionaryVft = 0x1_40577598;
    private const ulong TextureVft = 0x1_40617568;
    private const ulong TextureReferenceVft = 0x1_406187F8;
    private const ulong ModelVft = 0x1_4060EA98;
    private const ulong GeometryVft = 0x1_40616798;
    private const ulong VertexBufferVft = 0x1_4061B3F8;
    private const ulong IndexBufferVft = 0x1_4061B158;

    // Position, normal, colour 0 and 1, two UV sets and a tangent: 64 bytes, as in the game's props.
    private const uint VertexFlags = 0x40F9;
    private const ulong VertexTypes = 0x7755555555996996;
    private const int VertexStride = 64;

    private const float LodDistance = 9998;
    private const uint RenderMask = 0xFF01;
    private const uint NormalUsage = 0x2000B016;
    private const uint SpecularUsage = 0x2000B017;

    private enum Value { Texture, Vector }

    private sealed record Parameter(string Name, byte Slot, Value Kind, Vector4 Data = default, ulong Texture = 0);

    public static ResourceFile CreateProp(PropDrawable prop)
    {
        var mesh = prop.Mesh;
        if (mesh.VertexCount is 0 or > ushort.MaxValue)
            throw new ArgumentException($"A prop needs between 1 and {ushort.MaxValue} vertices, got {mesh.VertexCount}.", nameof(prop));

        var b = new ResourceBuilder();
        var root = b.Reserve(0x40);
        var vertices = b.Add(Vertices(mesh));
        var indices = b.Add(Indices(mesh));
        var drawable = b.Reserve(0xD0);
        var (min, max) = MeshBounds.Of([mesh]);

        b.UInt64(drawable, DrawableVft);
        b.UInt64(drawable + 0x10, ShaderGroup(b, prop));
        var center = (min + max) / 2;
        WriteVector(b, drawable + 0x20, center, Radius(mesh, center));
        WriteVector(b, drawable + 0x30, min, BitConverter.UInt32BitsToSingle(0x7F800001));
        WriteVector(b, drawable + 0x40, max, BitConverter.UInt32BitsToSingle(0x7F800001));
        for (var lod = 0; lod < 4; lod++) b.Float(drawable + 0x70 + (ulong)(lod * 4), LodDistance);
        b.UInt32(drawable + 0x80, RenderMask);
        var (models, modelsSize) = Models(b, mesh, vertices, indices, min, max);
        b.UInt64(drawable + 0x50, models);
        b.UInt16(drawable + 0x9A, (ushort)(modelsSize / 16));
        b.UInt64(drawable + 0xA0, models);
        b.UInt64(drawable + 0xA8, b.AddString(prop.Name));

        var hash = b.Reserve(4);
        b.UInt32(hash, Jenkins.Hash(prop.Name));
        var items = b.Reserve(8);
        b.UInt64(items, drawable);
        var pages = b.Reserve(0x20);
        b.UInt8(pages + 8, 1);
        b.UInt8(pages + 9, 1);

        b.UInt64(root, DictionaryVft);
        b.UInt64(root + 0x08, pages);
        b.UInt64(root + 0x18, 1);
        b.Array(root + 0x20, hash, 1);
        b.Array(root + 0x30, items, 1);
        return b.Build(DrawableDictionary.ResourceVersion);
    }

    private static ulong ShaderGroup(ResourceBuilder b, PropDrawable prop)
    {
        // Group, shader list and shader sit together; the group records their size in 16-byte units.
        var group = b.Reserve(0x40);
        var list = b.Reserve(8);
        var shader = b.Reserve(0x30);
        var parameters = new Parameter[]
        {
            new("DiffuseSampler", 0, Value.Texture, Texture: TextureReference(b, prop.DiffuseName)),
            new("VolumeSampler", 3, Value.Texture, Texture: TextureReference(b, "givemechecker")),
            new("BumpSampler", 4, Value.Texture),
            new("SpecSampler", 5, Value.Texture),
            new("umGlobalParams", 0xBB, Value.Vector, new Vector4(0, 0, 7, 7)),
            new("envEffFatThickness", 0xBA, Value.Vector, new Vector4(25, 25, 0, 0)),
            new("specularIntensityMult", 0xB9, Value.Vector, new Vector4(1, 0, 0, 0)),
            new("specularFalloffMult", 0xB8, Value.Vector, new Vector4(250, 0, 0, 0)),
            new("specularFresnel", 0xB7, Value.Vector, new Vector4(0.96f, 0, 0, 0)),
            new("bumpiness", 0xB6, Value.Vector, new Vector4(1, 0, 0, 0)),
            new("detailSettings", 0xB5, Value.Vector, new Vector4(0.1f, 0.75f, 40, 0)),
            new("StubbleControl", 0xB4, Value.Vector, new Vector4(2, 0.6f, 0, 0)),
        };

        var (dictionary, embedded) = EmbeddedTextures(b, [(prop.Normal, NormalUsage), (prop.Specular, SpecularUsage)]);
        parameters[2] = parameters[2] with { Texture = embedded[0] };
        parameters[3] = parameters[3] with { Texture = embedded[1] };

        var vectors = parameters.Count(p => p.Kind == Value.Vector);
        var headerSize = parameters.Length * 16;
        var dataSize = headerSize + vectors * 16;
        var blockSize = dataSize + parameters.Length * 4 + 32;
        var block = b.Reserve(blockSize);
        var data = block + (ulong)headerSize;
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var entry = block + (ulong)(i * 16);
            b.UInt8(entry, (byte)(p.Kind == Value.Vector ? 1 : 0));
            b.UInt8(entry + 1, p.Slot);
            if (p.Kind == Value.Texture)
            {
                b.UInt64(entry + 8, p.Texture);
                continue;
            }
            b.UInt64(entry + 8, data);
            WriteVector(b, data, new Vector3(p.Data.X, p.Data.Y, p.Data.Z), p.Data.W);
            data += 16;
        }
        var hashes = block + (ulong)dataSize;
        for (var i = 0; i < parameters.Length; i++)
            b.UInt32(hashes + (ulong)(i * 4), Jenkins.Hash(parameters[i].Name));

        b.UInt64(shader, block);
        b.UInt32(shader + 0x08, Jenkins.Hash("ped"));
        b.UInt8(shader + 0x10, (byte)parameters.Length);
        b.UInt16(shader + 0x12, 0x8000);
        b.UInt16(shader + 0x14, (ushort)dataSize);
        b.UInt16(shader + 0x16, (ushort)blockSize);
        b.UInt32(shader + 0x18, Jenkins.Hash("ped.sps"));
        b.UInt32(shader + 0x20, RenderMask);
        b.UInt8(shader + 0x27, (byte)parameters.Count(p => p.Kind == Value.Texture));

        b.UInt64(list, shader);
        b.UInt64(group, ShaderGroupVft);
        b.UInt64(group + 0x08, dictionary);
        b.Array(group + 0x10, list, 1);
        b.UInt32(group + 0x30, (uint)(shader + 0x30 - group) / 16);
        return group;
    }

    // A named slot the game fills from the garment's .ytd.
    private static ulong TextureReference(ResourceBuilder b, string name)
    {
        var texture = b.Reserve(0x50);
        b.UInt64(texture, TextureReferenceVft);
        b.UInt64(texture + 0x28, b.AddString(name));
        b.UInt32(texture + 0x30, 0x00020001);
        return texture;
    }

    // The dictionary lists textures sorted by name hash, which the game binary-searches.
    private static (ulong Dictionary, ulong[] Textures) EmbeddedTextures(ResourceBuilder b, IReadOnlyList<(EmbeddedTexture Texture, uint Usage)> textures)
    {
        var pointers = new ulong[textures.Count];
        for (var i = 0; i < textures.Count; i++)
        {
            var (embedded, usage) = textures[i];
            var t = embedded.Texture;
            var texture = b.Reserve(0x90);
            b.UInt64(texture, TextureVft);
            b.UInt64(texture + TextureDictionary.TextureName, b.AddString(embedded.Name));
            b.UInt32(texture + 0x30, 0x00800001);
            b.UInt32(texture + 0x40, usage);
            b.UInt16(texture + TextureDictionary.TextureWidth, (ushort)t.Width);
            b.UInt16(texture + TextureDictionary.TextureHeight, (ushort)t.Height);
            b.UInt16(texture + 0x54, 1);
            b.UInt16(texture + TextureDictionary.TextureStride, (ushort)TextureDictionaryWriter.Stride(t.Format, t.Width));
            b.UInt32(texture + TextureDictionary.TextureFormat, TextureDictionaryWriter.FormatCode(t.Format));
            b.UInt8(texture + TextureDictionary.TextureLevels, (byte)t.Mips.Count);
            b.UInt64(texture + TextureDictionary.TextureData, b.AddGraphics(t.Mips));
            pointers[i] = texture;
        }

        var sorted = textures.Select((t, i) => (Hash: Jenkins.Hash(t.Texture.Name), Pointer: pointers[i])).OrderBy(t => t.Hash).ToList();
        var hashes = b.Reserve(sorted.Count * 4);
        var items = b.Reserve(sorted.Count * 8);
        for (var i = 0; i < sorted.Count; i++)
        {
            b.UInt32(hashes + (ulong)(i * 4), sorted[i].Hash);
            b.UInt64(items + (ulong)(i * 8), sorted[i].Pointer);
        }
        var dictionary = b.Reserve(0x40);
        b.UInt64(dictionary, TextureDictionaryVft);
        b.UInt64(dictionary + 0x18, 1);
        b.Array(dictionary + 0x20, hashes, sorted.Count);
        b.Array(dictionary + 0x30, items, sorted.Count);
        return (dictionary, pointers);
    }

    // The high-detail model list with its one model and geometry, laid out back to back.
    private static (ulong List, int Size) Models(ResourceBuilder b, MeshData mesh, ulong vertices, ulong indices, Vector3 min, Vector3 max)
    {
        var list = b.Reserve(0x10);
        var listItems = b.Reserve(8);
        var model = b.Reserve(0x30);
        var geometries = b.Reserve(8);
        var shaderMap = b.Reserve(2);
        var bounds = b.Reserve(0x20);
        var geometry = b.Reserve(0x98);

        b.Array(list, listItems, 1);
        b.UInt64(listItems, model);

        b.UInt64(model, ModelVft);
        b.Array(model + 0x08, geometries, 1);
        b.UInt64(model + 0x18, bounds);
        b.UInt64(model + 0x20, shaderMap);
        b.UInt16(model + 0x2C, 0xFF);
        b.UInt16(model + 0x2E, 1);
        b.UInt64(geometries, geometry);
        WriteVector(b, bounds, min, 0);
        WriteVector(b, bounds + 0x10, max, 0);

        b.UInt64(geometry, GeometryVft);
        b.UInt64(geometry + 0x18, VertexBuffer(b, mesh, vertices));
        b.UInt64(geometry + 0x38, IndexBuffer(b, mesh, indices));
        b.UInt32(geometry + 0x58, (uint)mesh.Indices.Length);
        b.UInt32(geometry + 0x5C, (uint)mesh.TriangleCount);
        b.UInt16(geometry + 0x60, (ushort)mesh.VertexCount);
        b.UInt16(geometry + 0x62, 3);
        b.UInt16(geometry + 0x70, VertexStride);
        b.UInt64(geometry + 0x78, vertices);
        return (list, (int)(geometry + 0x98 - list));
    }

    private static ulong VertexBuffer(ResourceBuilder b, MeshData mesh, ulong vertices)
    {
        var declaration = b.Reserve(0x10);
        b.UInt32(declaration, VertexFlags);
        b.UInt16(declaration + 4, VertexStride);
        b.UInt8(declaration + 7, (byte)BitOperations.PopCount(VertexFlags));
        b.UInt64(declaration + 8, VertexTypes);

        var buffer = b.Reserve(0x80);
        b.UInt64(buffer, VertexBufferVft);
        b.UInt16(buffer + 0x08, VertexStride);
        b.UInt64(buffer + 0x10, vertices);
        b.UInt32(buffer + 0x18, (uint)mesh.VertexCount);
        b.UInt64(buffer + 0x20, vertices);
        b.UInt64(buffer + 0x30, declaration);
        return buffer;
    }

    private static ulong IndexBuffer(ResourceBuilder b, MeshData mesh, ulong indices)
    {
        var buffer = b.Reserve(0x60);
        b.UInt64(buffer, IndexBufferVft);
        b.UInt32(buffer + 0x08, (uint)mesh.Indices.Length);
        b.UInt64(buffer + 0x10, indices);
        return buffer;
    }

    // Colour 0 carries shading hints in ped models; this is their most common value on props.
    private static byte[] Vertices(MeshData mesh)
    {
        var tangents = MeshTangents.Compute(mesh);
        var data = new byte[mesh.VertexCount * VertexStride];
        for (var v = 0; v < mesh.VertexCount; v++)
        {
            var o = data.AsSpan(v * VertexStride, VertexStride);
            for (var c = 0; c < 3; c++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(o[(c * 4)..], mesh.Positions[v * 3 + c]);
                BinaryPrimitives.WriteSingleLittleEndian(o[(12 + c * 4)..], mesh.Normals[v * 3 + c]);
            }
            o[24] = 255;
            o[25] = 128;
            o[27] = 255;
            BinaryPrimitives.WriteSingleLittleEndian(o[32..], mesh.TexCoords[v * 2]);
            BinaryPrimitives.WriteSingleLittleEndian(o[36..], mesh.TexCoords[v * 2 + 1]);
            BinaryPrimitives.WriteSingleLittleEndian(o[40..], 1);
            BinaryPrimitives.WriteSingleLittleEndian(o[48..], tangents[v].X);
            BinaryPrimitives.WriteSingleLittleEndian(o[52..], tangents[v].Y);
            BinaryPrimitives.WriteSingleLittleEndian(o[56..], tangents[v].Z);
            BinaryPrimitives.WriteSingleLittleEndian(o[60..], tangents[v].W);
        }
        return data;
    }

    private static byte[] Indices(MeshData mesh)
    {
        var data = new byte[mesh.Indices.Length * 2];
        for (var i = 0; i < mesh.Indices.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), mesh.Indices[i]);
        return data;
    }

    private static float Radius(MeshData mesh, Vector3 center)
    {
        var radius = 0f;
        for (var i = 0; i < mesh.Positions.Length; i += 3)
            radius = MathF.Max(radius, Vector3.Distance(center, new Vector3(mesh.Positions[i], mesh.Positions[i + 1], mesh.Positions[i + 2])));
        return radius;
    }

    private static void WriteVector(ResourceBuilder b, ulong at, Vector3 v, float w)
    {
        b.Float(at, v.X);
        b.Float(at + 4, v.Y);
        b.Float(at + 8, v.Z);
        b.Float(at + 12, w);
    }
}
