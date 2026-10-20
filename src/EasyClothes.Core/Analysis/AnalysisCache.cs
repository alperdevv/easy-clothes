using System.Collections.Concurrent;

namespace EasyClothes.Core.Analysis;

public sealed class AnalysisCache
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, long Length, object Report)> _entries = new();

    public ModelReport Model(string path) => Get(path, AssetAnalyzer.AnalyzeModel);
    public TextureReport Texture(string path) => Get(path, AssetAnalyzer.AnalyzeTexture);

    private T Get<T>(string path, Func<string, T> analyze) where T : notnull
    {
        var info = new FileInfo(path);
        if (_entries.TryGetValue(path, out var entry) && entry.Stamp == info.LastWriteTimeUtc && entry.Length == info.Length)
            return (T)entry.Report;

        var report = analyze(path);
        _entries[path] = (info.LastWriteTimeUtc, info.Length, report);
        return report;
    }
}
