using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Linqraft.Core;

/// <summary>
/// A wrapper around an immutable array that provides value-based equality.
/// This is essential for incremental generator caching to work correctly.
/// </summary>
/// <typeparam name="T">The type of elements in the array.</typeparam>
public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    /// <summary>
    /// The underlying immutable array.
    /// </summary>
    private readonly ImmutableArray<T> _array;

    /// <summary>
    /// Creates a new EquatableArray from an existing immutable array.
    /// </summary>
    public EquatableArray(ImmutableArray<T> array)
    {
        _array = array.IsDefault ? ImmutableArray<T>.Empty : array;
    }

    /// <summary>
    /// Creates a new EquatableArray from an enumerable.
    /// </summary>
    public EquatableArray(IEnumerable<T>? items)
    {
        _array = items?.ToImmutableArray() ?? ImmutableArray<T>.Empty;
    }

    /// <summary>
    /// Gets an empty EquatableArray.
    /// </summary>
    public static EquatableArray<T> Empty => new(ImmutableArray<T>.Empty);

    /// <summary>
    /// Gets the underlying immutable array.
    /// </summary>
    public ImmutableArray<T> AsImmutableArray() =>
        _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    /// <summary>
    /// Gets the number of elements in the array.
    /// </summary>
    public int Count => _array.IsDefault ? 0 : _array.Length;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    public T this[int index] =>
        _array.IsDefault ? throw new IndexOutOfRangeException() : _array[index];

    /// <inheritdoc/>
    public bool Equals(EquatableArray<T> other)
    {
        var thisArray = _array.IsDefault ? ImmutableArray<T>.Empty : _array;
        var otherArray = other._array.IsDefault ? ImmutableArray<T>.Empty : other._array;

        if (thisArray.Length != otherArray.Length)
            return false;

        for (int i = 0; i < thisArray.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(thisArray[i], otherArray[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var array = _array.IsDefault ? ImmutableArray<T>.Empty : _array;
        unchecked
        {
            int hash = 17;
            foreach (var item in array)
            {
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator()
    {
        var array = _array.IsDefault ? ImmutableArray<T>.Empty : _array;
        return ((IEnumerable<T>)array).GetEnumerator();
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Equality operator.
    /// </summary>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) =>
        left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) =>
        !left.Equals(right);

    /// <summary>
    /// Implicit conversion from ImmutableArray.
    /// </summary>
    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);

    /// <summary>
    /// Implicit conversion to ImmutableArray.
    /// </summary>
    public static implicit operator ImmutableArray<T>(EquatableArray<T> array) =>
        array.AsImmutableArray();
}

/// <summary>
/// Extension methods for creating EquatableArray instances.
/// </summary>
public static class EquatableArrayExtensions
{
    /// <summary>
    /// Converts an enumerable to an EquatableArray.
    /// </summary>
    public static EquatableArray<T> ToEquatableArray<T>(this IEnumerable<T>? source)
        where T : IEquatable<T>
    {
        return new EquatableArray<T>(source);
    }

    /// <summary>
    /// Converts an immutable array to an EquatableArray.
    /// </summary>
    public static EquatableArray<T> ToEquatableArray<T>(this ImmutableArray<T> source)
        where T : IEquatable<T>
    {
        return new EquatableArray<T>(source);
    }
}
