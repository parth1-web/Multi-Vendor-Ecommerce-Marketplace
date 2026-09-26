using System.Security.Cryptography;

namespace Marketplace.Domain.Common;

/// <summary>
/// Time-ordered UUID (version 7) generator for .NET 8, where
/// <c>Guid.CreateVersion7</c> is not yet available.
/// </summary>
/// <remarks>
/// The first 48 bits carry a Unix millisecond timestamp so that inserts into a
/// PostgreSQL B-tree primary key stay append-friendly (fewer page splits, far less
/// index bloat under write-heavy workloads). The remaining bits are random, so
/// identifiers stay non-enumerable — unlike a sequential <c>int</c> key.
/// </remarks>
public static class SequentialGuid
{
    /// <summary>Creates a new time-ordered, non-enumerable identifier.</summary>
    public static Guid New() => New(DateTimeOffset.UtcNow);

    /// <summary>Creates a new identifier stamped with the supplied moment.</summary>
    public static Guid New(DateTimeOffset timestamp)
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);

        var unixMs = timestamp.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        // version 7
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70);
        // RFC 4122 variant
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        return new Guid(bytes, bigEndian: true);
    }
}
