using System.Collections.ObjectModel;

namespace BackgroundStudio.Core;

public sealed record EditState(EditOptions Options, string? BackgroundPath);

public sealed record EditSnapshot(Guid Id, string Label, EditState State);

public sealed class EditHistory
{
    public const int MaximumEntries = 24;

    public ObservableCollection<EditSnapshot> Items { get; } = [];

    public EditSnapshot? Selected { get; private set; }

    public void Reset(string label, EditState state)
    {
        Items.Clear();
        Commit(label, state);
    }

    public bool Commit(string label, EditState state)
    {
        if (Items.LastOrDefault()?.State == state)
        {
            Selected = Items[^1];
            return false;
        }

        var snapshot = new EditSnapshot(Guid.NewGuid(), label, state);
        Items.Add(snapshot);
        while (Items.Count > MaximumEntries)
        {
            Items.RemoveAt(0);
        }
        Selected = snapshot;
        return true;
    }

    public EditSnapshot? Select(Guid id)
    {
        Selected = Items.FirstOrDefault(item => item.Id == id);
        return Selected;
    }

    public bool Select(EditState state)
    {
        Selected = Items.LastOrDefault(item => item.State == state);
        return Selected is not null;
    }
}
