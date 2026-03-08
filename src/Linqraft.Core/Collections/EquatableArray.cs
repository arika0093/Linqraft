using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Linqraft.Core.Collections;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _items;

    public EquatableArray(IEnumerable<T> items)
    {
        _items = items.ToImmutableArray();
    }

    public int Length => _items.Length;

    public bool IsDefaultOrEmpty => _items.IsDefaultOrEmpty;

    public T this[int index] => _items[index];

    public ImmutableArray<T> ToImmutableArray() => _items.IsDefault ? ImmutableArray<T>.Empty : _items;

    public bool Equals(EquatableArray<T> other)
    {
        if (Length != other.Length)
        {
            return false;
        }

        for (var index = 0; index < Length; index++)
        {
            if (!_items[index].Equals(other._items[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is EquatableArray<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in _items)
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        return (_items.IsDefault ? ImmutableArray<T>.Empty : _items).AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public static implicit operator EquatableArray<T>(T[] items)
    {
        return new EquatableArray<T>(items);
    }

    public static implicit operator EquatableArray<T>(ImmutableArray<T> items)
    {
        return new EquatableArray<T>(items.IsDefault ? Array.Empty<T>() : items);
    }
}
