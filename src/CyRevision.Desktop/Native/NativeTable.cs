using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CyRevision.Desktop.Native;

public static class NativeTable
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Columns = new();
    private sealed class RowIdentity { public long Value { get; } = Interlocked.Increment(ref _nextIdentity); }
    private static readonly ConditionalWeakTable<object, RowIdentity> Identities = new();
    private static long _nextIdentity;
    private static readonly string[] Preferred = ["Number", "Name", "FileName", "Commit", "Author", "Date", "Change", "Storage", "Size", "Message", "Path", "RelativePath", "StateText", "CiStateText", "Title", "OwnershipText", "UpdatedText", "Event", "Branch", "State", "Status", "Kind", "Timestamp", "Subject", "IsIncluded"];
    public static PropertyInfo[] Properties(Type type) => Columns.GetOrAdd(type, key =>
        key.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetMethod is not null && property.GetIndexParameters().Length == 0 &&
                NativeWorkspaceCatalog.IsScalar(property.PropertyType) && !NativeWorkspaceCatalog.IsSecret(property.Name) &&
                !property.Name.EndsWith("Color", StringComparison.Ordinal) && !property.Name.EndsWith("Brush", StringComparison.Ordinal))
            .OrderBy(property => Array.IndexOf(Preferred, property.Name) is int rank && rank >= 0 ? rank : 100)
            .Take(24).ToArray());

    public static string Key(object? item) => item is null ? "" : NativeWorkspaceCatalog.IsScalar(item.GetType())
        ? item.ToString() ?? "" : Identities.GetValue(item, _ => new RowIdentity()).Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static object? Read(PropertyInfo property, object instance)
    {
        try { return property.GetValue(instance); }
        catch (TargetInvocationException) { return null; }
    }

    public static object? Value(object? value, int maximum = 4096)
    {
        if (value is null) return null;
        if (value is double number && !double.IsFinite(number) || value is float single && !float.IsFinite(single)) return null;
        if (value is string text) return text.Length <= maximum ? text : text[..maximum] + " …";
        if (value.GetType().IsEnum || value is Guid or DateTime or DateTimeOffset or TimeSpan) return value.ToString();
        return value;
    }

    public static object Page(IList list, string name, int skip, int take, string filter, string sort, bool descending)
    {
        take = Math.Clamp(take, 1, 200);
        skip = Math.Clamp(skip, 0, Math.Max(0, list.Count));
        Type? type = list.Count > 0 ? list[0]?.GetType() : null;
        PropertyInfo[] columns = type is null || NativeWorkspaceCatalog.IsScalar(type) ? [] : Properties(type);
        if(name.Contains("LogLines",StringComparison.Ordinal) && type is not null)
            columns=new[]{"Number","Source","Text"}.Select(property=>type.GetProperty(property)).Where(property=>property is not null).Cast<PropertyInfo>().ToArray();
        IEnumerable<int> indexes = Enumerable.Range(0, list.Count);
        if (!string.IsNullOrWhiteSpace(filter))
            indexes = indexes.Where(index => columns.Length == 0
                ? (list[index]?.ToString()?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false)
                : columns.Any(column => Read(column, list[index]!)?.ToString()?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true));
        PropertyInfo? sortProperty = columns.FirstOrDefault(property => property.Name == sort);
        if (sortProperty is not null)
            indexes = descending
                ? indexes.OrderByDescending(index => Read(sortProperty, list[index]!), ScalarComparer.Instance)
                : indexes.OrderBy(index => Read(sortProperty, list[index]!), ScalarComparer.Instance);
        // Without a filter/sort, use the collection's count and indexed access:
        // a 60k-change workspace transfers only the visible page.
        int[]? matched = string.IsNullOrWhiteSpace(filter) && sortProperty is null ? null : indexes.ToArray();
        int total = matched?.Length ?? list.Count;
        if (skip >= total) skip = total == 0 ? 0 : ((total - 1) / take) * take;
        var rows = (matched ?? indexes).Skip(skip).Take(take).Select(index =>
        {
            object? item = list[index];
            Dictionary<string, object?> values = columns.Length == 0
                ? new() { ["Value"] = Value(item) }
                : columns.ToDictionary(column => column.Name, column => Value(Read(column, item!),name.Contains("LogLines",StringComparison.Ordinal) || column.Name is "Body" or "Message"?262144:4096));
            if(name.Contains("LogLines",StringComparison.Ordinal) && item is CyRevision.PullRequests.CiLogLine line){values["IsError"]=line.IsError;values["IsWarning"]=line.IsWarning;}
            return new { index, key = Key(item), values };
        }).ToArray();
        int maxTextColumns=name.Contains("LogLines",StringComparison.Ordinal)?list.OfType<CyRevision.PullRequests.CiLogLine>().Select(line=>line.Text.Length).DefaultIfEmpty(0).Max():0;
        return new { name, total, skip, take, filter, sort, descending, maxTextColumns, columns = columns.Length == 0 ? ["Value"] : columns.Select(column => column.Name).ToArray(), rows };
    }

    private sealed class ScalarComparer : IComparer<object?>
    {
        public static readonly ScalarComparer Instance = new();
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            if (x.GetType() == y.GetType() && x is IComparable comparable) return comparable.CompareTo(y);
            return StringComparer.OrdinalIgnoreCase.Compare(x.ToString(), y.ToString());
        }
    }
}
