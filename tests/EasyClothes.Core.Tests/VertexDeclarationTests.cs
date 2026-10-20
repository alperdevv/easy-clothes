using EasyClothes.Core.Rage;

namespace EasyClothes.Core.Tests;

public class VertexDeclarationTests
{
    // Type word used by every ped drawable in the sample packs.
    private const ulong PedTypes = 0x7755555555996996;

    [Theory]
    [InlineData(0x7Fu, 48)]
    [InlineData(0xFFu, 56)]
    [InlineData(0x40FFu, 72)]
    [InlineData(0x40F9u, 64)]
    [InlineData(0x405Fu, 60)]
    public void Stride_is_the_sum_of_present_components(uint flags, int stride) =>
        Assert.Equal(stride, new VertexDeclaration(flags, PedTypes).Stride);

    [Fact]
    public void Components_are_packed_in_semantic_order()
    {
        var decl = new VertexDeclaration(0x40F9, PedTypes);

        Assert.Equal(0, decl.OffsetOf(VertexSemantic.Position));
        Assert.Equal(-1, decl.OffsetOf(VertexSemantic.BlendWeights));
        Assert.Equal(12, decl.OffsetOf(VertexSemantic.Normal));
        Assert.Equal(32, decl.OffsetOf(VertexSemantic.TexCoord0));
        Assert.Equal(VertexComponentType.Float2, decl.TypeOf(VertexSemantic.TexCoord0));
        Assert.Equal(48, decl.OffsetOf(VertexSemantic.Tangent));
    }
}
