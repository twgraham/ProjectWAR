using System.Numerics;
using System.Runtime.CompilerServices;

namespace WorldServerV2.Network;

/// <summary>
/// Reads packed bitstreams in LSB-first order (matching the WAR client's encoding).
/// <para>
/// The client writes bits starting from the least-significant bit of each byte, advancing
/// to the next byte when all 8 bits are consumed. Multi-bit values are written LSB-first,
/// so this reader extracts them in the same order.
/// </para>
/// <para>
/// This is a <c>ref struct</c> for stack-only performance — no heap allocations. Pass by
/// <c>ref</c> to mutate the cursor position.
/// </para>
/// </summary>
public ref struct BitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _bitPosition;

    public BitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bitPosition = 0;
    }

    /// <summary>Current bit position in the stream.</summary>
    public readonly int BitPosition => _bitPosition;

    /// <summary>Number of bits remaining in the stream.</summary>
    public readonly int BitsRemaining => _data.Length * 8 - _bitPosition;

    /// <summary>Reads a single bit as a boolean.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBit()
    {
        int byteIndex = _bitPosition >> 3;       // _bitPosition / 8
        int bitIndex = _bitPosition & 7;          // _bitPosition % 8
        _bitPosition++;
        return (_data[byteIndex] >> bitIndex & 1) != 0;
    }

    /// <summary>
    /// Reads <paramref name="count"/> bits as an unsigned integer (LSB-first).
    /// Maximum 32 bits.
    /// </summary>
    public uint ReadBits(int count)
    {
        uint value = 0;
        for (int i = 0; i < count; i++)
        {
            int byteIndex = _bitPosition >> 3;
            int bitIndex = _bitPosition & 7;
            value |= (uint)((_data[byteIndex] >> bitIndex) & 1) << i;
            _bitPosition++;
        }

        return value;
    }

    /// <summary>
    /// Reads a ranged value encoded as <c>value − min</c> in the minimum number of bits
    /// needed to represent the range <c>[min, max]</c>.
    /// <para>
    /// The bit count is computed by <see cref="BitsForRange"/> to match the client's
    /// <c>WriteRanged</c> encoding (sub_4332D4).
    /// </para>
    /// </summary>
    public int ReadRanged(int min, int max)
    {
        int range = Math.Abs(max - min) + 1;
        int bitCount = BitsForRange(range);
        return (int)ReadBits(bitCount) + min;
    }

    /// <summary>
    /// Reads a signed value: 1 sign bit followed by <c>totalBits − 1</c> magnitude bits.
    /// Matches the client's <c>WriteSigned</c> (sub_433364).
    /// </summary>
    public int ReadSigned(int totalBits)
    {
        uint magnitude = ReadBits(totalBits - 1);
        bool negative = ReadBit();
        return negative ? -(int)magnitude : (int)magnitude;
    }

    /// <summary>
    /// Reads a float encoded as a signed integer scaled to a fixed-point range.
    /// Matches the client's <c>WriteFloat</c> (sub_4333FE): the value was clamped
    /// to <c>[0, maxValue]</c>, scaled to <c>2^(totalBits−1) − 1</c>, then written
    /// via <see cref="ReadSigned"/>.
    /// </summary>
    /// <param name="totalBits">Number of bits (sign + magnitude), e.g. 12.</param>
    /// <param name="maxValue">Maximum float value (before scaling).</param>
    /// <returns>The reconstructed float value.</returns>
    public float ReadFloat(int totalBits, float maxValue)
    {
        int raw = ReadSigned(totalBits);
        float scale = (1 << (totalBits - 1)) - 1;
        return raw * maxValue / scale;
    }

    /// <summary>Skips <paramref name="count"/> bits without reading them.</summary>
    public void Skip(int count)
    {
        _bitPosition += count;
    }

    /// <summary>
    /// Computes the number of bits needed for a WriteRanged field: the smallest <c>n</c>
    /// where <c>2^n &gt; range</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BitsForRange(int range)
    {
        if (range <= 1) return 1; // Client writes 1 bit even for a single-value range
        // 32 - LeadingZeroCount gives the position of highest set bit + 1
        // If range is an exact power of 2, we still need one more bit (client uses strict >).
        int bits = 32 - BitOperations.LeadingZeroCount((uint)(range - 1));
        if ((1 << bits) <= range)
            bits++;
        return bits;
    }
}
