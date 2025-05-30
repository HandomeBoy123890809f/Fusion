using System.Diagnostics.CodeAnalysis;
using ActualLab.Collections.Internal;
using MessagePack;

namespace ActualLab.Collections;

#pragma warning disable CS0618 // Type or member is obsolete

#if !NET5_0
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptOut)]
public readonly partial struct PropertyBag : IEquatable<PropertyBag>
{
    public static readonly PropertyBag Empty;

    private readonly PropertyBagItem[]? _items;

    // MessagePack requires this member to be public
    [Obsolete("This member exists solely to make serialization work. Don't use it!")]
    [DataMember(Order = 0), MemoryPackOrder(0), Key(0), MemoryPackInclude, JsonInclude, Newtonsoft.Json.JsonProperty]
    public PropertyBagItem[]? RawItems {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items;
        init => _items = value != null && value.Length != 0 ? value : null;
    }

    // Computed properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public int Count {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items?.Length ?? 0;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore, IgnoreMember]
    public IReadOnlyList<PropertyBagItem> Items {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _items ?? [];
    }

    public object? this[string key] {
        get {
            if (_items == null || key.IsNullOrEmpty())
                return null;

            var index = Array.IndexOf(_items, PropertyBagItem.NewKey(key));
            return index >= 0 ? _items[index].Value : null;
        }
    }

    public object? this[Type key] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this[key.ToIdentifierSymbol()];
    }

    public PropertyBag()
    { }

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, MemoryPackConstructor, SerializationConstructor]
    public PropertyBag(PropertyBagItem[]? rawItems)
    {
        if (rawItems != null && rawItems.Length != 0)
            _items = rawItems.SortInPlace(PropertyBagItem.Comparer);
    }

    public override string ToString()
        => $"{nameof(PropertyBag)}({PropertyBagHelper.GetToStringArgs(_items)})";

    public PropertyBag Set(string key, object? value)
    {
        if (value == null)
            return Remove(key);

        var item = PropertyBagItem.New(key, value);
        if (_items == null)
            return new PropertyBag([item]);

        var index = Array.IndexOf(_items, item);
        PropertyBagItem[] items;
        if (index >= 0) {
            items = new PropertyBagItem[_items.Length];
            _items.AsSpan().CopyTo(items.AsSpan());
        }
        else {
            items = new PropertyBagItem[_items.Length + 1];
            _items.AsSpan().CopyTo(items.AsSpan());
            index = items.Length - 1;
        }
        items[index] = item;
        items.SortInPlace(PropertyBagItem.Comparer);
        return new PropertyBag(items);
    }

    public PropertyBag SetMany(params ReadOnlySpan<PropertyBagItem> items)
    {
        var buffer = ArrayBuffer<PropertyBagItem>.Lease(true);
        try {
            foreach (var item in _items ?? [])
                buffer.Add(item);
            foreach (var item in items) {
                var span = buffer.Span;
                var index = span.IndexOf(item);
                if (index >= 0)
                    span[index] = item;
                else
                    buffer.Add(item);
            }
            return new PropertyBag(buffer.ToArray().SortInPlace(PropertyBagItem.Comparer));
        }
        finally {
            buffer.Release();
        }
    }

    public PropertyBag Remove(string key)
    {
        if (key.IsNullOrEmpty())
            throw new ArgumentOutOfRangeException(nameof(key));
        if (_items == null)
            return this;

        var index = Array.IndexOf(_items, PropertyBagItem.NewKey(key));
        if (index < 0)
            return this;
        if (_items.Length == 1)
            return default;

        var items = new PropertyBagItem[_items.Length - 1];
        _items.AsSpan(0, index).CopyTo(items.AsSpan());
        _items.AsSpan(index + 1).CopyTo(items.AsSpan(index));
        items.SortInPlace(PropertyBagItem.Comparer);
        return new PropertyBag(items);
    }

    // Equality

    public bool Equals(PropertyBag other) => ReferenceEquals(_items, other._items);
    public override bool Equals(object? obj) => obj is PropertyBag other && Equals(other);
    public override int GetHashCode() => _items == null ? 0 : RuntimeHelpers.GetHashCode(_items);
    public static bool operator ==(PropertyBag x, PropertyBag y) => Equals(x, y);
    public static bool operator !=(PropertyBag x, PropertyBag y) => !Equals(x, y);
}
