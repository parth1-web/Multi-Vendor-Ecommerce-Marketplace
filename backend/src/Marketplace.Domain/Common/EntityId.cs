using System.Diagnostics.CodeAnalysis;

namespace Marketplace.Domain.Common;

/// <summary>
/// Time-ordered, non-enumerable identifier (UUIDv7 layout). Created in the domain so
/// entity ids never depend on database-generated defaults, and so PostgreSQL B-tree
/// inserts stay append-friendly.
/// </summary>
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    private EntityId(Guid value) => Value = value;

    public Guid Value { get; }

    public static EntityId New() => new(SequentialGuid.New());

    public static EntityId From(Guid value) => new(value);

    public static implicit operator Guid(EntityId id) => id.Value;

    public static implicit operator EntityId(Guid value) => new(value);

    public bool IsEmpty => Value == Guid.Empty;

    public bool Equals(EntityId other) => Value.Equals(other.Value);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is EntityId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();

    public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

    public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
}
