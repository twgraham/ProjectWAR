using System.IO;

namespace LauncherServer.Utils;

/// <summary>
/// Provides Adler-32 checksum computation for patch file verification.
/// Replaces the legacy FrameWork.Utils.Adler32 dependency.
/// </summary>
internal static class Adler32
{
    private const uint ModAdler = 65521;

    public static uint Compute(uint adler, byte[] bytes, ulong length)
    {
        uint s1 = adler & 0xFFFF;
        uint s2 = adler >> 16;
        for (ulong i = 0; i < length; i++)
        {
            s1 = (s1 + bytes[i]) % ModAdler;
            s2 = (s2 + s1) % ModAdler;
        }
        return unchecked((uint)((s2 << 16) + s1));
    }

    public static uint Compute(Stream stream, long size, int blockSize = 0xFFFFF, uint adler = 0)
    {
        long remaining = size;
        var block = new byte[blockSize];
        uint s1 = adler & 0xFFFF;
        uint s2 = adler >> 16;

        while (remaining > 0)
        {
            int readSize = (int)Math.Min(blockSize, remaining);
            int bytesRead = stream.Read(block, 0, readSize);
            for (int i = 0; i < bytesRead; i++)
            {
                s1 = (s1 + block[i]) % ModAdler;
                s2 = (s2 + s1) % ModAdler;
            }
            remaining -= bytesRead;
        }

        return unchecked((uint)((s2 << 16) + s1));
    }
}
