namespace EasyClothes.Core.Validation;

public enum IssueSeverity
{
    Error,
    Warning,
    Info,
}

public abstract record FixAction(string Label);

public sealed record ResizeTextureFix(string TexturePath, int MaxSide) : FixAction($"Reducir a {MaxSide}px");

public sealed record RegenerateMipsFix(string TexturePath) : FixAction("Generar mipmaps");

public sealed record ReduceMeshFix(string ModelPath, int TargetTriangles) : FixAction($"Reducir a {TargetTriangles:N0} triángulos");

public sealed record ValidationIssue(IssueSeverity Severity, string Message, Guid? DrawableId = null, FixAction? Fix = null);

public sealed record AnalysisLimits
{
    public const int FiveMOversizedBytes = 16 * 1024 * 1024;

    public int MaxTextureSide { get; init; } = 2048;
    public int MaxHighLodTriangles { get; init; } = 15_000;
    public double UvOutOfRangeRatio { get; init; } = 0.05;
}
