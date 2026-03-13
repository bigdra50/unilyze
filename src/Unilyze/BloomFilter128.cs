using System.Numerics;
using System.Runtime.CompilerServices;

namespace Unilyze;

public readonly struct BloomFilter128 : IEquatable<BloomFilter128>
{
    readonly UInt128 _bits;

    public BloomFilter128(UInt128 bits)
    {
        _bits = bits;
    }

    public static BloomFilter128 Empty => new(0);

    public bool IsEmpty => _bits == 0;

    public UInt128 Bits => _bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BloomFilter128 SetBit(int bitIndex)
    {
        bitIndex &= 0x7F;
        return new(_bits | (UInt128.One << bitIndex));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BloomFilter128 Union(BloomFilter128 other)
    {
        return new(_bits | other._bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BloomFilter128 Intersect(BloomFilter128 other)
    {
        return new(_bits & other._bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int PopCount()
    {
        var lower = (ulong)_bits;
        var upper = (ulong)(_bits >> 64);
        return BitOperations.PopCount(lower) + BitOperations.PopCount(upper);
    }

    public static ulong SimpleHash(string s, ulong multiplier)
    {
        ulong hash = 0;
        foreach (var c in s)
            hash = unchecked(hash * multiplier + c);
        return hash;
    }

    public bool MightContain(string value)
    {
        var hash1 = SimpleHash(value, 31);
        var hash2 = SimpleHash(value, 37);
        var hash3 = SimpleHash(value, 41);

        var filter = Empty
            .SetBit((int)(hash1 % 128))
            .SetBit((int)(hash2 % 128))
            .SetBit((int)(hash3 % 128));

        return (filter._bits & _bits) == filter._bits;
    }

    public BloomFilter128 Add(string value)
    {
        var hash1 = SimpleHash(value, 31);
        var hash2 = SimpleHash(value, 37);
        var hash3 = SimpleHash(value, 41);

        return SetBit((int)(hash1 % 128))
            .SetBit((int)(hash2 % 128))
            .SetBit((int)(hash3 % 128));
    }

    public BloomFilter128 Add(uint value)
    {
        var h1 = SimpleHashUInt(value, 31);
        var h2 = SimpleHashUInt(value, 37);
        var h3 = SimpleHashUInt(value, 41);

        return SetBit((int)(h1 % 128))
            .SetBit((int)(h2 % 128))
            .SetBit((int)(h3 % 128));
    }

    public double EstimateSimilarity(BloomFilter128 other)
    {
        var selfPop = PopCount();
        var otherPop = other.PopCount();
        if (selfPop == 0 && otherPop == 0) return 1.0;
        var intersection = Intersect(other).PopCount();
        return (double)intersection / Math.Max(selfPop, otherPop);
    }

    static ulong SimpleHashUInt(uint value, ulong multiplier) =>
        unchecked(value * multiplier + 0x9e3779b9);

    public bool Equals(BloomFilter128 other) => _bits == other._bits;
    public override bool Equals(object? obj) => obj is BloomFilter128 other && Equals(other);
    public override int GetHashCode() => _bits.GetHashCode();
    public static bool operator ==(BloomFilter128 left, BloomFilter128 right) => left.Equals(right);
    public static bool operator !=(BloomFilter128 left, BloomFilter128 right) => !left.Equals(right);
}
