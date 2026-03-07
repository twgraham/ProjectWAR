using System;

namespace WorldServer.NetWork.V2;

public static class SpanExtensions
{
    public static ushort ComputeChecksum(this Span<byte> data)
    {
        byte sum1 = 0x7E;
        byte sum2 = 0x7E;
        
        foreach (var b in data)
        {
            sum1 = (byte)(sum1 + b);
            sum2 = (byte)(sum2 + sum1);
        }
        
        return (ushort)((sum1 << 8) | sum2);
    }
}