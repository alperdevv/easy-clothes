using System.Buffers.Binary;
using System.Numerics;

namespace EasyClothes.Core.Rage;

public sealed record Bone(string Name, ushort Tag, int Parent, Matrix4x4 World);

// Bind pose of a ped skeleton, read from its fragment (.yft). World = bone space to model space.
public static class Skeleton
{
    public const uint FragmentVersion = 162;
    public const ushort HeadTag = 31086;

    private const ulong FragmentDrawable = 0x30;
    private const ulong DrawableSkeleton = 0x18;
    private const ulong SkeletonBones = 0x20;
    private const ulong SkeletonBoneCount = 0x5E;
    private const int BoneSize = 0x50;

    public static IReadOnlyList<Bone> ReadFragment(ResourceFile fragment)
    {
        if (fragment.Header.Version != FragmentVersion)
            throw new InvalidDataException($"Expected a fragment (version {FragmentVersion}), got version {fragment.Header.Version}.");

        var reader = new ResourceReader(fragment);
        var drawable = reader.UInt64(0x50000000 + FragmentDrawable);
        var skeleton = drawable == 0 ? 0 : reader.UInt64(drawable + DrawableSkeleton);
        if (skeleton == 0) throw new InvalidDataException("The fragment has no skeleton.");

        var first = reader.UInt64(skeleton + SkeletonBones);
        var count = reader.UInt16(skeleton + SkeletonBoneCount);
        var bones = new Bone[count];
        for (var i = 0; i < count; i++)
        {
            var b = reader.At(first + (ulong)(i * BoneSize), BoneSize);
            var rotation = new Quaternion(Float(b, 0x00), Float(b, 0x04), Float(b, 0x08), Float(b, 0x0C));
            var translation = new Vector3(Float(b, 0x10), Float(b, 0x14), Float(b, 0x18));
            var parent = BinaryPrimitives.ReadInt16LittleEndian(b[0x32..]);
            var local = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            if (parent >= i) throw new InvalidDataException($"Bone {i} has parent {parent}, which comes after it.");
            bones[i] = new Bone(
                reader.CString(BinaryPrimitives.ReadUInt64LittleEndian(b[0x38..])) ?? "",
                BinaryPrimitives.ReadUInt16LittleEndian(b[0x44..]),
                parent,
                parent >= 0 ? local * bones[parent].World : local);
        }
        return bones;
    }

    private static float Float(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadSingleLittleEndian(data[offset..]);
}
