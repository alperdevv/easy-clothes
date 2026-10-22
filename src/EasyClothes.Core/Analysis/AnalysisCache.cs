using System.Collections.Concurrent;

namespace EasyClothes.Core.Analysis;

public sealed class AnalysisCache
{
    private readonly ConcurrentDictionary<(string Path, Type Kind), (DateTime Stamp, long Length, object Report)> _entries = new();

    public ModelReport Model(string path) => Get(path, AssetAnalyzer.AnalyzeModel);
    public TextureReport Texture(string path) => Get(path, AssetAnalyzer.AnalyzeTexture);
    public string Hash(string path) => Get(path, p => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p))));

    private T Get<T>(string path, Func<string, T> analyze) where T : notnull
    {
        var info = new FileInfo(path);
        if (_entries.TryGetValue((path, typeof(T)), out var entry) && entry.Stamp == info.LastWriteTimeUtc && entry.Length == info.Length)
            return (T)entry.Report;

        var report = analyze(path);
        _entries[(path, typeof(T))] = (info.LastWriteTimeUtc, info.Length, report);
        return report;
    }
}
