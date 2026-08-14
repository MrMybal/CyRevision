using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CyRevision.Desktop.ViewModels;

internal interface IBatchReplaceCollection
{
    void ReplaceAll(IEnumerable items);
}

/// <summary>
/// Replaces a collection with one Reset notification so virtualized controls
/// remain responsive with very large repositories.
/// </summary>
internal sealed class BatchObservableCollection<T> : ObservableCollection<T>, IBatchReplaceCollection
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        T[] snapshot = items as T[] ?? items.ToArray();

        Items.Clear();
        foreach (T item in snapshot)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    void IBatchReplaceCollection.ReplaceAll(IEnumerable items) => ReplaceAll(items.Cast<T>());
}
