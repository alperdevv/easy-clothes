namespace EasyClothes.Core.Textures.Editing;

public sealed record HistoryEntry(string Label, string? MergeKey, TextureDocument State);

// Each entry is a full document snapshot; pixel buffers are shared between entries, so the
// memory cost is the buffers that actually changed. Old entries are dropped past the budget.
public sealed class EditHistory
{
    private readonly List<HistoryEntry> _entries = [];
    private readonly long _budgetBytes;
    private int _savedIndex;

    public EditHistory(TextureDocument initial, string label, long budgetBytes = 768L * 1024 * 1024)
    {
        _budgetBytes = budgetBytes;
        _entries.Add(new HistoryEntry(label, null, initial.Snapshot()));
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries;
    public int Index { get; private set; }
    public bool CanUndo => Index > 0;
    public bool CanRedo => Index < _entries.Count - 1;
    public bool IsModified => Index != _savedIndex;

    // Consecutive commits with the same key (a slider being dragged) collapse into one entry.
    public void Commit(TextureDocument document, string label, string? mergeKey = null)
    {
        _entries.RemoveRange(Index + 1, _entries.Count - Index - 1);
        if (_savedIndex > Index) _savedIndex = -1;
        var entry = new HistoryEntry(label, mergeKey, document.Snapshot());
        if (mergeKey is not null && Index > 0 && _entries[Index].MergeKey == mergeKey && _savedIndex != Index)
            _entries[Index] = entry;
        else
        {
            _entries.Add(entry);
            Index++;
        }
        Trim();
    }

    public TextureDocument JumpTo(int index)
    {
        Index = Math.Clamp(index, 0, _entries.Count - 1);
        return _entries[Index].State;
    }

    public TextureDocument Undo() => JumpTo(Index - 1);
    public TextureDocument Redo() => JumpTo(Index + 1);

    public void MarkSaved() => _savedIndex = Index;

    private void Trim()
    {
        while (_entries.Count > 2 && Index > 0 && Bytes() > _budgetBytes)
        {
            _entries.RemoveAt(0);
            Index--;
            _savedIndex--;
        }
    }

    private long Bytes() => _entries
        .SelectMany(e => e.State.Layers)
        .SelectMany(l => l switch { PixelLayer p => [p.Image.Pixels], ImageLayer i => [i.Image.Pixels], _ => Array.Empty<byte[]>() })
        .Distinct(ReferenceEqualityComparer.Instance)
        .Sum(p => (long)((byte[])p!).Length);
}
