using System.Numerics;
using EasyClothes.Core.Rage;
using EasyClothes.Core.Viewing;

namespace EasyClothes.Core.Modeling;

// What the shirt's texture painter needs at a vertex: how much it belongs to a sleeve (0 torso, 1 arm), how
// far it lies from the nearest opening (neck, cuff or hem) in metres, where it is on the body, and 1 on a collar.
public readonly record struct ShirtPaint(float Sleeve, float Edge, Vector3 Position, float Collar);

// Points of the body the painter places details by: the neck bone, the left arm's joints and armpit
// (mirror them for the right), waist and chest heights, and the body's vertical axis.
public sealed record ShirtLandmarks(Vector3 Neck, float NeckRadius, Vector3 Shoulder, Vector3 Elbow, Vector3 Wrist, Vector3 Armpit,
    float Waist, float Chest, Vector2 Axis)
{
    public float UpperArm => Vector3.Distance(Shoulder, Elbow);

    // Height on the torso, or distance from the shoulder down the arm on a sleeve.
    public float Along(Vector3 p, bool sleeve)
    {
        if (!sleeve) return p.Z;
        var q = Mirror(p);
        var along = Vector3.Dot(q - Shoulder, Vector3.Normalize(Elbow - Shoulder));
        return along <= UpperArm ? along : UpperArm + Vector3.Dot(q - Elbow, Vector3.Normalize(Wrist - Elbow));
    }

    // Arc length round the torso from the front (towards the wearer's left), or round the arm from its outside.
    public float Around(Vector3 p, bool sleeve)
    {
        if (!sleeve) return MathF.Atan2(p.X - Axis.X, -(p.Y - Axis.Y)) * 0.15f;
        var q = Mirror(p);
        var (start, end) = Along(p, true) <= UpperArm ? (Shoulder, Elbow) : (Elbow, Wrist);
        var axis = Vector3.Normalize(end - start);
        var outward = Vector3.Normalize(Vector3.UnitX - axis * axis.X);
        var forward = Vector3.Cross(axis, outward);
        var offset = q - start - axis * Vector3.Dot(q - start, axis);
        return MathF.Atan2(Vector3.Dot(offset, forward), Vector3.Dot(offset, outward)) * 0.045f;
    }

    // The right side seen as the left one: both arms share their texture.
    public Vector3 Mirror(Vector3 p) => p with { X = Axis.X + MathF.Abs(p.X - Axis.X) };
}

public sealed record ShirtMesh(MeshData Mesh, ShirtPaint[] Paints, ShirtLandmarks Landmarks);

// Cuts a shirt out of the bare freemode torso and dresses it. The body's bone weights come along, so the
// shirt follows every movement without weight painting. Works in the ped's bind pose: +Z up, +X to the
// wearer's left, -Y forward. UVs are the body's, with the torso squeezed up to leave room for a longer hem.
public static class ShirtModel
{
    // The torso is the lower half of the body's texture; it gives up its bottom to the hem extension.
    public const float TorsoTop = 0.5f;
    public const float TorsoScale = 0.8f;
    public const float HemBand = 0.098f;

    private const float TorsoS = -0.15f;
    private const float Armhole = -0.05f;
    private const int HemRings = 4;
    private const float RimDepth = 0.005f;
    // The longest hem column: the longest shirt, a curved hem and how much higher the body's edge is at the back.
    private const float MaxDrop = ShirtDesign.MaxLength / 100 + 0.21f;
    private const float HemCurve = 0.06f;
    private const float CuffRoll = 0.035f;

    public static ShirtMesh Build(ShirtDesign design, SkinnedBody body)
    {
        var d = design.Clamped();
        if (d.Collar != Collar.None) d = d with { Neckline = Neckline.Crew, NeckDepth = 0 };
        var rig = Rig.From(body.Skeleton);
        var work = Work.From(body.Mesh, rig);

        var waist = work.WaistHeight();
        var neckRadius = work.NeckRadius(rig.Neck);
        float HemZ(Vector3 p) => waist - d.Length / 100 - HemDrop(d.Hem, p, rig.Hips);
        for (var v = 0; v < work.Count; v++)
            work.Fields[v] = new Vector3(rig.SleeveField(work, v, d.Sleeve), work.Positions[v].Z - HemZ(work.Positions[v]) + work.Arm[v], rig.NeckField(work, v, d));

        for (var field = 0; field < 3; field++) work.Clip(field);
        work.Compact();

        var undershirt = d.Layer == ShirtLayer.Undershirt;
        work.Inflate(work.Weld(),
            undershirt ? Lerp(0.003f, 0.014f, d.Fit) : Lerp(0.006f, 0.028f, d.Fit),
            undershirt ? Lerp(0.0025f, 0.01f, d.Fit) : Lerp(0.005f, 0.02f, d.Fit),
            d.RolledCuffs);
        for (var v = 0; v < work.Count; v++)
            if (work.Uvs[v].Y > TorsoTop) work.Uvs[v] = work.Uvs[v] with { Y = TorsoTop + (work.Uvs[v].Y - TorsoTop) * TorsoScale };
        work.ExtendHem(work.Weld(), HemZ, Lerp(0.008f, 0.03f, d.Fit), rig);
        if (body.Legs is { } legs) work.ClearOf(new Profile(legs, rig.Hips), Lerp(0.008f, 0.02f, d.Fit));
        if (d.Collar != Collar.None) work.AddCollar(work.Weld(), d.Collar, rig, neckRadius);
        work.AddRims(work.Weld());

        var (shoulder, elbow, wrist) = rig.LeftArm;
        var landmarks = new ShirtLandmarks(rig.Neck, neckRadius, shoulder, elbow, wrist, shoulder + new Vector3(-0.03f, 0, -0.1f),
            waist, rig.Neck.Z - 0.15f, new Vector2(rig.Hips.X, rig.Hips.Y));
        return work.ToMesh(landmarks);
    }

    // How much lower than the straight hem a point's hem sits: a curved hem dips at the front and back,
    // a longer back only behind.
    private static float HemDrop(HemShape hem, Vector3 p, Vector3 hips)
    {
        var offset = new Vector2(p.X - hips.X, p.Y - hips.Y);
        if (offset == Vector2.Zero || hem == HemShape.Straight) return 0;
        var direction = Vector2.Normalize(offset);
        return hem == HemShape.Curved ? HemCurve * direction.Y * direction.Y : HemCurve * MathF.Max(0, direction.Y);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothStep(float from, float to, float x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    // Arm chains and the bones a shirt needs, found by name in the ped skeleton.
    private sealed class Rig
    {
        private enum Part { Torso, Upper, Fore, Hand }

        private readonly Part[] _parts;
        private readonly bool[] _left;
        private readonly (Vector3 Shoulder, Vector3 Elbow, Vector3 Wrist)[] _arms = new (Vector3, Vector3, Vector3)[2];

        public Vector3 Hips { get; }
        public Vector3 Neck { get; }
        public int Pelvis { get; }
        public int NeckBone { get; }
        public int LeftThigh { get; }
        public int RightThigh { get; }

        public (Vector3 Shoulder, Vector3 Elbow, Vector3 Wrist) LeftArm => _arms[0];
        public float Chain => Vector3.Distance(_arms[0].Shoulder, _arms[0].Elbow) + Vector3.Distance(_arms[0].Elbow, _arms[0].Wrist);

        private Rig(IReadOnlyList<Bone> bones)
        {
            _parts = bones.Select(b => Classify(b.Name)).ToArray();
            _left = bones.Select(b => b.Name.Contains("_L_", StringComparison.Ordinal)).ToArray();
            Vector3 At(string name) => bones.FirstOrDefault(b => b.Name == name)?.World.Translation
                ?? throw new InvalidDataException($"The ped skeleton has no {name}.");
            _arms[0] = (At("SKEL_L_UpperArm"), At("SKEL_L_Forearm"), At("SKEL_L_Hand"));
            _arms[1] = (At("SKEL_R_UpperArm"), At("SKEL_R_Forearm"), At("SKEL_R_Hand"));
            Neck = At("SKEL_Neck_1");
            Hips = At("SKEL_Pelvis");
            int Index(string name) => bones.ToList().FindIndex(b => b.Name == name) is var i and >= 0 ? i
                : throw new InvalidDataException($"The ped skeleton has no {name}.");
            Pelvis = Index("SKEL_Pelvis");
            NeckBone = Index("SKEL_Neck_1");
            LeftThigh = Index("SKEL_L_Thigh");
            RightThigh = Index("SKEL_R_Thigh");
        }

        public static Rig From(IReadOnlyList<Bone> bones) => new(bones);

        private static Part Classify(string name)
        {
            if (!name.Contains("_L_", StringComparison.Ordinal) && !name.Contains("_R_", StringComparison.Ordinal)) return Part.Torso;
            if (name.Contains("Forearm", StringComparison.OrdinalIgnoreCase) || name.Contains("Elbow", StringComparison.Ordinal)) return Part.Fore;
            if (name.Contains("UpperArm", StringComparison.Ordinal) || name.Contains("ArmRoll", StringComparison.Ordinal)) return Part.Upper;
            if (name.Contains("Hand", StringComparison.Ordinal) || name.Contains("Finger", StringComparison.Ordinal)) return Part.Hand;
            return Part.Torso;
        }

        public float Armness(Skin skin)
        {
            var arm = 0f;
            for (var k = 0; k < 4; k++)
                if (_parts[skin.Bone(k)] != Part.Torso) arm += skin.Weight(k);
            return arm;
        }

        // Position along the arm, 0 at the shoulder joint and 1 at the wrist, blended by the bone weights
        // so the armhole follows the body's own shoulder seam. Torso bones count as before the shoulder.
        public float AlongArm(Vector3 p, Skin skin)
        {
            var s = 0f;
            for (var k = 0; k < 4; k++)
            {
                var bone = skin.Bone(k);
                var (shoulder, elbow, wrist) = _arms[_left[bone] ? 0 : 1];
                var upper = Vector3.Distance(shoulder, elbow);
                var fore = Vector3.Distance(elbow, wrist);
                var along = _parts[bone] switch
                {
                    Part.Upper => Vector3.Dot(p - shoulder, Vector3.Normalize(elbow - shoulder)),
                    Part.Fore => upper + Vector3.Dot(p - elbow, Vector3.Normalize(wrist - elbow)),
                    Part.Hand => upper + fore + MathF.Max(0, Vector3.Dot(p - wrist, Vector3.Normalize(wrist - elbow))),
                    _ => TorsoS * (upper + fore),
                };
                s += skin.Weight(k) * along / (upper + fore);
            }
            return s;
        }

        // Metres of sleeve left beyond the vertex: positive is kept. No sleeve cuts into the shoulder, where
        // the weights blend into the torso, for a real armhole; a full one stops 2 cm short of the wrist.
        public float SleeveField(Work work, int v, float sleeve)
        {
            var cut = Lerp(Armhole, 1 - 0.02f / Chain, sleeve);
            return (cut - AlongArm(work.Positions[v], work.Skins[v])) * Chain;
        }

        // Metres below the neckline: the back sits at the base of the neck and the front drops by the style.
        public float NeckField(Work work, int v, ShirtDesign d)
        {
            var p = work.Positions[v];
            var front = SmoothStep(Neck.Y + 0.01f, Neck.Y - 0.06f, p.Y);
            var depth = 0.02f + d.NeckDepth / 100;
            var shape = d.Neckline switch
            {
                Neckline.VNeck => MathF.Max(0, 1 - MathF.Abs(p.X) / 0.075f),
                Neckline.Scoop => MathF.Max(0, 1 - p.X * p.X / (0.1f * 0.1f)),
                _ => MathF.Max(0, 1 - p.X * p.X / (0.075f * 0.075f)),
            };
            var limit = Neck.Z - 0.012f - front * depth * shape;
            return limit - p.Z + work.Arm[v];
        }
    }

    // How far the trousers reach from the body's vertical axis, by height and direction. A garment
    // vertex closer to the axis than the trousers at that spot gets pushed out past them.
    private sealed class Profile
    {
        private const float Bottom = -0.4f;
        private const float Step = 0.01f;
        private const int Heights = 80;
        private const int Directions = 72;

        private readonly float[] _radius = new float[Heights * Directions];
        private readonly Vector2 _axis;

        public Profile(MeshData mesh, Vector3 hips)
        {
            _axis = new Vector2(hips.X, hips.Y);
            for (var t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                var (a, b, c) = (Point(mesh, mesh.Indices[t]), Point(mesh, mesh.Indices[t + 1]), Point(mesh, mesh.Indices[t + 2]));
                // Corners, edge midpoints and centre, so large triangles leave no gaps in the bins.
                foreach (var p in new[] { a, b, c, (a + b) / 2, (b + c) / 2, (c + a) / 2, (a + b + c) / 3 }) Record(p);
            }
        }

        private static Vector3 Point(MeshData mesh, int v) => new(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]);

        private (int Height, int Direction, float Radius) Bin(Vector3 p)
        {
            var offset = new Vector2(p.X, p.Y) - _axis;
            var angle = MathF.Atan2(offset.Y, offset.X);
            var direction = (int)MathF.Floor((angle + MathF.PI) / MathF.Tau * Directions) % Directions;
            return ((int)MathF.Floor((p.Z - Bottom) / Step), direction, offset.Length());
        }

        private void Record(Vector3 p)
        {
            var (h, d, r) = Bin(p);
            if (h < 0 || h >= Heights) return;
            _radius[h * Directions + d] = MathF.Max(_radius[h * Directions + d], r);
        }

        public Vector3 PushOut(Vector3 p, float clearance)
        {
            var (h, d, r) = Bin(p);
            var needed = 0f;
            for (var dh = -1; dh <= 1; dh++)
            {
                for (var dd = -1; dd <= 1; dd++)
                {
                    var hh = h + dh;
                    if (hh < 0 || hh >= Heights) continue;
                    needed = MathF.Max(needed, _radius[hh * Directions + (d + dd + Directions) % Directions]);
                }
            }
            if (needed == 0 || r >= needed + clearance || r < 1e-4f) return p;
            var offset = new Vector2(p.X, p.Y) - _axis;
            var pushed = _axis + offset * ((needed + clearance) / r);
            return new Vector3(pushed.X, pushed.Y, p.Z);
        }
    }

    // Up to four bones with weights that add up to 1.
    internal readonly record struct Skin(uint Bones, Vector4 Weights)
    {
        public int Bone(int k) => (int)((Bones >> (8 * k)) & 0xFF);
        public float Weight(int k) => Weights[k];

        public static Skin FromBytes(ReadOnlySpan<byte> weights, ReadOnlySpan<byte> bones) => new(
            (uint)(bones[0] | bones[1] << 8 | bones[2] << 16 | bones[3] << 24),
            new Vector4(weights[0], weights[1], weights[2], weights[3]) / 255);

        public static Skin Single(int bone) => new((uint)bone, new Vector4(1, 0, 0, 0));

        // Blends two skins and keeps the four strongest bones.
        public static Skin Mix(Skin a, float wa, Skin b, float wb)
        {
            Span<int> bones = stackalloc int[8];
            Span<float> weights = stackalloc float[8];
            var count = 0;
            for (var k = 0; k < 8; k++)
            {
                var (bone, w) = k < 4 ? (a.Bone(k), a.Weight(k) * wa) : (b.Bone(k - 4), b.Weight(k - 4) * wb);
                if (w <= 0) continue;
                var at = bones[..count].IndexOf(bone);
                if (at >= 0)
                {
                    weights[at] += w;
                    continue;
                }
                bones[count] = bone;
                weights[count++] = w;
            }

            uint packed = 0;
            var kept = Vector4.Zero;
            for (var k = 0; k < 4 && k < count; k++)
            {
                var best = k;
                for (var i = k + 1; i < count; i++) if (weights[i] > weights[best]) best = i;
                (bones[k], bones[best]) = (bones[best], bones[k]);
                (weights[k], weights[best]) = (weights[best], weights[k]);
                packed |= (uint)bones[k] << (8 * k);
                kept[k] = weights[k];
            }
            var total = kept.X + kept.Y + kept.Z + kept.W;
            return total > 0 ? new Skin(packed, kept / total) : a;
        }

        // Bytes that add up to exactly 255, as the game's files do.
        public void Write(Span<byte> weights, Span<byte> bones)
        {
            var sum = 0;
            for (var k = 0; k < 4; k++)
            {
                weights[k] = (byte)MathF.Round(Weights[k] * 255);
                bones[k] = (byte)Bone(k);
                sum += weights[k];
            }
            weights[0] = (byte)Math.Clamp(weights[0] + 255 - sum, 0, 255);
        }
    }

    // The mesh while it's being cut and dressed: plain lists so vertices and triangles can be added freely.
    private sealed class Work
    {
        public readonly List<Vector3> Positions = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Vector2> Uvs = [];
        public readonly List<Skin> Skins = [];
        public readonly List<float> Arm = [];
        public readonly List<Vector3> Fields = [];
        public readonly List<float> OnCollar = [];
        public List<int> Triangles = [];

        public int Count => Positions.Count;

        public static Work From(MeshData mesh, Rig rig)
        {
            var work = new Work();
            for (var v = 0; v < mesh.VertexCount; v++)
            {
                var skin = Skin.FromBytes(mesh.BlendWeights.AsSpan(v * 4, 4), mesh.BlendIndices.AsSpan(v * 4, 4));
                work.Add(
                    new Vector3(mesh.Positions[v * 3], mesh.Positions[v * 3 + 1], mesh.Positions[v * 3 + 2]),
                    new Vector3(mesh.Normals[v * 3], mesh.Normals[v * 3 + 1], mesh.Normals[v * 3 + 2]),
                    new Vector2(mesh.TexCoords[v * 2], mesh.TexCoords[v * 2 + 1]),
                    skin, rig.Armness(skin), default, 0);
            }
            work.Triangles = mesh.Indices.Select(i => (int)i).ToList();
            return work;
        }

        private int Add(Vector3 position, Vector3 normal, Vector2 uv, Skin skin, float arm, Vector3 fields, float collar)
        {
            Positions.Add(position);
            Normals.Add(normal);
            Uvs.Add(uv);
            Skins.Add(skin);
            Arm.Add(arm);
            Fields.Add(fields);
            OnCollar.Add(collar);
            return Positions.Count - 1;
        }

        private int Copy(int v, Vector3 position, Vector3 normal) => Add(position, normal, Uvs[v], Skins[v], Arm[v], Fields[v], OnCollar[v]);

        // The bottom of the torso skin, ignoring the arms that hang lower.
        public float WaistHeight() => Enumerable.Range(0, Count).Where(v => Arm[v] < 0.5f).Min(v => Positions[v].Z);

        // The body's neck where the skin ends at the top, measured round the neck bone.
        public float NeckRadius(Vector3 neck)
        {
            var ring = Enumerable.Range(0, Count).Where(v => Arm[v] < 0.5f && Positions[v].Z > neck.Z).ToList();
            return ring.Count == 0 ? 0.065f : ring.Average(v => new Vector2(Positions[v].X - neck.X, Positions[v].Y - neck.Y).Length());
        }

        // Keeps the part where the field is positive, splitting triangles on the cut so its edge is smooth.
        public void Clip(int field)
        {
            var cuts = new Dictionary<(int, int), int>();
            var kept = new List<int>(Triangles.Count);
            Span<int> polygon = stackalloc int[4];
            for (var t = 0; t < Triangles.Count; t += 3)
            {
                var count = 0;
                for (var e = 0; e < 3; e++)
                {
                    var a = Triangles[t + e];
                    var b = Triangles[t + (e + 1) % 3];
                    var fa = Fields[a][field];
                    var fb = Fields[b][field];
                    if (fa >= 0) polygon[count++] = a;
                    if (fa >= 0 != fb >= 0) polygon[count++] = Cut(cuts, a, b, fa / (fa - fb));
                }
                for (var i = 2; i < count; i++) kept.AddRange([polygon[0], polygon[i - 1], polygon[i]]);
            }
            Triangles = kept;
        }

        // One new vertex per cut edge, shared by both triangles on it.
        private int Cut(Dictionary<(int, int), int> cuts, int a, int b, float t)
        {
            var key = a < b ? (a, b) : (b, a);
            if (cuts.TryGetValue(key, out var existing)) return existing;
            var v = Add(
                Vector3.Lerp(Positions[a], Positions[b], t),
                Vector3.Normalize(Vector3.Lerp(Normals[a], Normals[b], t)),
                Vector2.Lerp(Uvs[a], Uvs[b], t),
                Skin.Mix(Skins[a], 1 - t, Skins[b], t),
                Arm[a] + (Arm[b] - Arm[a]) * t,
                Vector3.Lerp(Fields[a], Fields[b], t),
                0);
            cuts[key] = v;
            return v;
        }

        public void Compact()
        {
            var map = new int[Count];
            Array.Fill(map, -1);
            var order = new List<int>();
            foreach (var v in Triangles)
            {
                if (map[v] >= 0) continue;
                map[v] = order.Count;
                order.Add(v);
            }
            void Keep<T>(List<T> list)
            {
                var copy = order.Select(v => list[v]).ToList();
                list.Clear();
                list.AddRange(copy);
            }
            Keep(Positions);
            Keep(Normals);
            Keep(Uvs);
            Keep(Skins);
            Keep(Arm);
            Keep(Fields);
            Keep(OnCollar);
            Triangles = Triangles.Select(v => map[v]).ToList();
        }

        // Vertices at the same spot (split by UV seams) share a group, so the surface stays closed.
        public int[] Weld()
        {
            var groups = new int[Count];
            var seen = new Dictionary<(int, int, int), int>();
            for (var v = 0; v < Count; v++)
            {
                var p = Positions[v] * 20000;
                var key = ((int)MathF.Round(p.X), (int)MathF.Round(p.Y), (int)MathF.Round(p.Z));
                groups[v] = seen.TryGetValue(key, out var g) ? g : seen[key] = v;
            }
            return groups;
        }

        private Vector3[] GroupNormals(int[] groups)
        {
            var sums = new Vector3[Count];
            for (var v = 0; v < Count; v++) sums[groups[v]] += Normals[v];
            return Enumerable.Range(0, Count).Select(v => sums[groups[v]] == Vector3.Zero ? Normals[v] : Vector3.Normalize(sums[groups[v]])).ToArray();
        }

        // Moves the surface off the skin: more on the torso than on the sleeves, and a roll at turned-up cuffs.
        public void Inflate(int[] groups, float torso, float arm, bool rolledCuffs)
        {
            var normals = GroupNormals(groups);
            for (var v = 0; v < Count; v++)
            {
                var roll = rolledCuffs && Arm[v] > 0.5f ? 0.006f * MathF.Sqrt(MathF.Max(0, 1 - Fields[v].X / CuffRoll)) : 0;
                Normals[v] = normals[v];
                Positions[v] += normals[v] * (torso + (arm - torso) * Arm[v] + roll);
            }
        }

        // Pushes the torso out over the trousers wherever they stick out further, e.g. belts and hips.
        public void ClearOf(Profile legs, float clearance)
        {
            for (var v = 0; v < Count; v++)
            {
                if (Arm[v] >= 0.5f) continue;
                Positions[v] = legs.PushOut(Positions[v], clearance);
            }
        }

        // Grows a strip of rings out of a boundary loop, continuing the surface across it; ring(v, t)
        // makes the vertex t of the way along for loop vertex v.
        private Dictionary<int, int[]> Extrude(IEnumerable<(int A, int B, int Opposite)> loop, int rings, Func<int, float, int> ring)
        {
            var made = new Dictionary<int, int[]>();
            int[] Ring(int v)
            {
                if (made.TryGetValue(v, out var existing)) return existing;
                var r = new int[rings + 1];
                r[0] = v;
                for (var k = 1; k <= rings; k++) r[k] = ring(v, (float)k / rings);
                return made[v] = r;
            }
            foreach (var (a, b, _) in loop)
            {
                var ra = Ring(a);
                var rb = Ring(b);
                for (var k = 0; k < rings; k++)
                    Triangles.AddRange([ra[k + 1], rb[k], ra[k], ra[k + 1], rb[k + 1], rb[k]]);
            }
            return made;
        }

        // Continues the bottom edge of the torso down to the hem, flaring a little, with the hips' bones.
        // The body's edge sits higher at the back, so each column stretches to its own hem height; columns
        // already at the hem (a cropped shirt) are left as they are.
        public void ExtendHem(int[] groups, Func<Vector3, float> hemZ, float flare, Rig rig)
        {
            var bottom = Loops(groups).MinBy(l => l.Average(e => Positions[e.A].Z)) ?? [];
            bottom = bottom.Where(e => Positions[e.A].Z - hemZ(Positions[e.A]) > 0.002f || Positions[e.B].Z - hemZ(Positions[e.B]) > 0.002f).ToList();
            if (bottom.Count == 0) return;
            Extrude(bottom, HemRings, (v, t) =>
            {
                var outward = new Vector3(Normals[v].X, Normals[v].Y, 0);
                outward = outward == Vector3.Zero ? Normals[v] : Vector3.Normalize(outward);
                var column = MathF.Max(0, Positions[v].Z - hemZ(Positions[v]));
                var thigh = Positions[v].X > 0 ? rig.LeftThigh : rig.RightThigh;
                var side = Math.Clamp((MathF.Abs(Positions[v].X) - 0.03f) / 0.09f, 0, 1);
                var hips = Skin.Mix(Skin.Single(rig.Pelvis), 1 - 0.5f * side, Skin.Single(thigh), 0.5f * side);
                var r = Copy(v, Positions[v] - Vector3.UnitZ * (column * t) + outward * (flare * t * t), outward);
                Uvs[r] = Uvs[v] with { Y = Uvs[v].Y + t * HemBand * MathF.Min(1, column / MaxDrop) };
                Skins[r] = Skin.Mix(Skins[v], 1 - 0.7f * t, hips, 0.7f * t);
                Fields[r] = Fields[v] with { Y = column * (1 - t) };
                return r;
            });
        }

        // A collar grown from the neckline, closing round the neck: a turtleneck rises up it, polo and shirt
        // collars stand a little and fold back down over the stand. The fold is seen from both sides, so it
        // is built twice, back to back. Collars move with the neck.
        public void AddCollar(int[] groups, Collar collar, Rig rig, float neckRadius)
        {
            var neckline = Loops(groups).MaxBy(l => l.Average(e => Positions[e.A].Z));
            if (neckline is null) return;
            var axis = new Vector2(rig.Neck.X, rig.Neck.Y);
            var (stand, fold) = collar switch
            {
                Collar.Turtleneck => (0.06f, 0f),
                Collar.Shirt => (0.03f, 0.045f),
                _ => (0.025f, 0.035f),
            };
            var standRings = collar == Collar.Turtleneck ? 5 : 2;
            var foldRings = fold > 0 ? 3 : 0;

            var rings = Extrude(neckline, standRings + foldRings, (v, t) =>
            {
                var k = t * (standRings + foldRings);
                var offset = new Vector2(Positions[v].X, Positions[v].Y) - axis;
                var direction = offset == Vector2.Zero ? Vector2.UnitY : Vector2.Normalize(offset);
                var radial = new Vector3(direction, 0);
                var front = MathF.Max(0, -direction.Y);
                // The top of the stand hugs the neck and sits lower at the front, under the chin.
                var standTop = new Vector3(axis + direction * (neckRadius + 0.012f), rig.Neck.Z + stand * (1 - 0.35f * front));
                var neck = Skin.Single(rig.NeckBone);
                int r;
                if (k <= standRings)
                {
                    var s = k / standRings;
                    var position = Vector3.Lerp(Positions[v], standTop, SmoothStep(0, 1, s)) with { Z = Positions[v].Z + (standTop.Z - Positions[v].Z) * s };
                    r = Copy(v, position, radial);
                    Fields[r] = new Vector3(1, 1, collar == Collar.Turtleneck ? stand * (1 - s) : stand * (1 - s) + fold);
                    Skins[r] = Skin.Mix(Skins[v], 1 - 0.85f * s, neck, 0.85f * s);
                }
                else
                {
                    var f = (k - standRings) / foldRings;
                    var reach = fold * (1 + 0.5f * front * (collar == Collar.Shirt ? 1 : 0));
                    var position = standTop + radial * (0.006f + 0.022f * f) - Vector3.UnitZ * (reach * f);
                    // The fold's own winding faces the stand; the copy below faces outwards.
                    r = Copy(v, position, -radial);
                    Fields[r] = new Vector3(1, 1, reach * (1 - f));
                    Skins[r] = Skin.Mix(Skins[v], 0.15f, neck, 0.85f);
                }
                OnCollar[r] = 1;
                Arm[r] = 0;
                return r;
            });

            if (foldRings == 0) return;
            var outer = new Dictionary<int, int>();
            int Outer(int v) => outer.TryGetValue(v, out var o) ? o : outer[v] = Copy(v, Positions[v], -Normals[v]);
            foreach (var (a, b, _) in neckline)
            {
                var (ra, rb) = (rings[a], rings[b]);
                for (var k = standRings; k < standRings + foldRings; k++)
                    Triangles.AddRange([Outer(ra[k]), Outer(rb[k]), Outer(ra[k + 1]), Outer(rb[k]), Outer(rb[k + 1]), Outer(ra[k + 1])]);
            }
        }

        // A narrow band folded in towards the body at every opening, so the cloth looks like it has thickness.
        public void AddRims(int[] groups)
        {
            var edges = BoundaryEdges(groups).ToList();
            var normals = GroupNormals(groups);
            var rims = new Dictionary<int, int>();
            var outward = new Dictionary<int, Vector3>();
            foreach (var (a, b, c) in edges.Select(e => (e.A, e.B, e.Opposite)))
            {
                var mid = (Positions[a] + Positions[b]) / 2;
                var across = mid - Positions[c];
                foreach (var v in new[] { a, b }) outward[v] = outward.GetValueOrDefault(v) + across;
            }
            int Rim(int v)
            {
                if (rims.TryGetValue(v, out var rim)) return rim;
                var o = outward[v] == Vector3.Zero ? normals[v] : Vector3.Normalize(outward[v]);
                rim = Copy(v, Positions[v] - normals[v] * RimDepth, o);
                Fields[rim] = Vector3.Zero;
                Normals[v] = Vector3.Normalize(Normals[v] + o * 0.5f);
                return rims[v] = rim;
            }
            foreach (var (a, b, _) in edges)
            {
                var ra = Rim(a);
                var rb = Rim(b);
                Triangles.AddRange([b, a, ra, b, ra, rb]);
            }
        }

        // The openings: connected runs of boundary edges.
        private IEnumerable<List<(int A, int B, int Opposite)>> Loops(int[] groups)
        {
            var edges = BoundaryEdges(groups).ToList();
            var parent = new Dictionary<int, int>();
            int Find(int g) => parent.TryGetValue(g, out var p) && p != g ? parent[g] = Find(p) : g;
            foreach (var (a, b, _) in edges) parent[Find(groups[a])] = Find(groups[b]);
            return edges.GroupBy(e => Find(groups[e.A])).Select(loop => loop.ToList())
                .Where(loop => loop.Average(e => Arm[e.A]) < 0.5f);
        }

        // Edges used by a single triangle once seam duplicates are merged, in the triangle's own order.
        private IEnumerable<(int A, int B, int Opposite)> BoundaryEdges(int[] groups)
        {
            var uses = new Dictionary<(int, int), int>();
            for (var t = 0; t < Triangles.Count; t += 3)
            {
                for (var e = 0; e < 3; e++)
                {
                    var a = groups[Triangles[t + e]];
                    var b = groups[Triangles[t + (e + 1) % 3]];
                    var key = a < b ? (a, b) : (b, a);
                    uses[key] = uses.GetValueOrDefault(key) + 1;
                }
            }
            for (var t = 0; t < Triangles.Count; t += 3)
            {
                for (var e = 0; e < 3; e++)
                {
                    var a = Triangles[t + e];
                    var b = Triangles[t + (e + 1) % 3];
                    var (ga, gb) = (groups[a], groups[b]);
                    if (uses[ga < gb ? (ga, gb) : (gb, ga)] == 1) yield return (a, b, Triangles[t + (e + 2) % 3]);
                }
            }
        }

        public ShirtMesh ToMesh(ShirtLandmarks landmarks)
        {
            if (Count > ushort.MaxValue) throw new InvalidOperationException("The shirt has too many vertices for 16-bit indices.");
            var weights = new byte[Count * 4];
            var bones = new byte[Count * 4];
            for (var v = 0; v < Count; v++) Skins[v].Write(weights.AsSpan(v * 4, 4), bones.AsSpan(v * 4, 4));
            var mesh = new MeshData
            {
                Positions = Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray(),
                Normals = Normals.SelectMany(n => new[] { n.X, n.Y, n.Z }).ToArray(),
                TexCoords = Uvs.SelectMany(uv => new[] { uv.X, uv.Y }).ToArray(),
                Indices = Triangles.Select(i => (ushort)i).ToArray(),
                BlendWeights = weights,
                BlendIndices = bones,
            };
            var paints = Enumerable.Range(0, Count)
                .Select(v => new ShirtPaint(Arm[v], MathF.Max(0, MathF.Min(Fields[v].X, MathF.Min(Fields[v].Y, Fields[v].Z))), Positions[v], OnCollar[v]))
                .ToArray();
            return new ShirtMesh(mesh, paints, landmarks);
        }
    }
}
