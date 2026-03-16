using System;

namespace WorldServerV2.Network;

public static class SpanExtensions
{
    public static ushort ComputeChecksum(this Span<byte> data)
    {
        byte sum1 = 0x7E;
        byte sum2 = 0x7E;
        
        foreach (var b in data)
        {
            sum1 += b;
            sum2 += sum1;
        }
        
        return (ushort)((-sum1 * 0x100) + (-sum2 * 0xFF));
    }
}