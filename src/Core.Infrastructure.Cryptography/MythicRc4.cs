namespace Core.Infrastructure.Cryptography;

/// <summary>
/// Provides extension methods and helper utilities for MythicRC4 encryption/decryption.
/// </summary>
public static class MythicRc4
{
    /// <summary>
    /// Encrypts data in-place using the Mythic RC4 algorithm.
    /// </summary>
    /// <param name="key">The 256-byte encryption key.</param>
    /// <param name="data">The buffer containing data to encrypt.</param>
    /// <param name="offset">The offset in the buffer where encryption should begin.</param>
    /// <param name="length">The number of bytes to encrypt.</param>
    public static void Encrypt(byte[] key, byte[] data, int offset, int length)
    {
        Encrypt(new ReadOnlySpan<byte>(key), data.AsSpan(offset, length));
    }
    
    /// <summary>
    /// Encrypts data in-place using the Mythic RC4 algorithm.
    /// </summary>
    /// <param name="key">The 256-byte encryption key.</param>
    /// <param name="data">The buffer containing data to encrypt.</param>
    public static void Encrypt(ReadOnlySpan<byte> key, Span<byte> data)
    {
        if (key.Length != 256)
            throw new ArgumentException("Key must be exactly 256 bytes", nameof(key));
        
        EncryptCore(key, data);
    }

    /// <summary>
    /// Decrypts data in-place using the Mythic RC4 algorithm.
    /// </summary>
    /// <param name="key">The 256-byte decryption key.</param>
    /// <param name="data">The buffer containing data to decrypt.</param>
    /// <param name="offset">The offset in the buffer where decryption should begin.</param>
    /// <param name="length">The number of bytes to decrypt.</param>
    public static void Decrypt(byte[] key, byte[] data, int offset, int length)
    {
        Decrypt(key, data.AsSpan(offset, length));
    }

    /// <summary>
    /// Decrypts data in-place using the Mythic RC4 algorithm.
    /// </summary>
    /// <param name="key">The 256-byte decryption key.</param>
    /// <param name="data">The buffer containing data to decrypt.</param>
    public static void Decrypt(ReadOnlySpan<byte> key, Span<byte> data)
    {
        if (key.Length != 256)
            throw new ArgumentException("Key must be exactly 256 bytes", nameof(key));
        
        DecryptCore(key, data);
    }
    
    private static void EncryptCore(ReadOnlySpan<byte> key, Span<byte> buffer)
    {
        var x = 0;
        var y = 0;
        var length = buffer.Length;
        var midpoint = length / 2;
        // Stack-allocate working key - zero heap allocations!
        Span<byte> workingKey = stackalloc byte[256];

        // Copy working key from original
        key.CopyTo(workingKey);

        // Process second half first (midpoint to end)
        for (var pos = midpoint; pos < length; ++pos)
        {
            x = (x + 1) & 255;
            y = (y + workingKey[x]) & 255;

            // Swap workingKey[x] and workingKey[y]
            (workingKey[x], workingKey[y]) = (workingKey[y], workingKey[x]);

            // Generate keystream byte
            var tmp = (byte)((workingKey[x] + workingKey[y]) & 255);
            
            // NON-STANDARD: Update y with the plaintext byte BEFORE encryption
            y = (y + buffer[pos]) & 255;
            
            // XOR with keystream
            buffer[pos] ^= workingKey[tmp];
        }

        // Process first half (start to midpoint)
        for (var pos = 0; pos < midpoint; ++pos)
        {
            x = (x + 1) & 255;
            y = (y + workingKey[x]) & 255;

            // Swap workingKey[x] and workingKey[y]
            (workingKey[x], workingKey[y]) = (workingKey[y], workingKey[x]);

            // Generate keystream byte
            var tmp = (byte)((workingKey[x] + workingKey[y]) & 255);
            
            // NON-STANDARD: Update y with the plaintext byte BEFORE encryption
            y = (y + buffer[pos]) & 255;
            
            // XOR with keystream
            buffer[pos] ^= workingKey[tmp];
        }
    }

    private static void DecryptCore(ReadOnlySpan<byte> key, Span<byte> buffer)
    {
        var x = 0;
        var y = 0;
        var length = buffer.Length;
        var midpoint = length / 2;
        // Stack-allocate working key - zero heap allocations!
        Span<byte> workingKey = stackalloc byte[256];

        // Copy working key from original
        key.CopyTo(workingKey);

        // Process second half first (midpoint to end)
        for (var pos = midpoint; pos < length; ++pos)
        {
            x = (x + 1) & 255;
            y = (y + workingKey[x]) & 255;

            // Swap workingKey[x] and workingKey[y]
            (workingKey[x], workingKey[y]) = (workingKey[y], workingKey[x]);

            // Generate keystream byte
            var tmp = (byte)((workingKey[x] + workingKey[y]) & 255);
            
            // XOR with keystream
            buffer[pos] ^= workingKey[tmp];
            
            // NON-STANDARD: Update y with the plaintext byte (after XOR)
            y = (y + buffer[pos]) & 255;
        }

        // Process first half (start to midpoint)
        for (var pos = 0; pos < midpoint; ++pos)
        {
            x = (x + 1) & 255;
            y = (y + workingKey[x]) & 255;

            // Swap workingKey[x] and workingKey[y]
            (workingKey[x], workingKey[y]) = (workingKey[y], workingKey[x]);

            // Generate keystream byte
            var tmp = (byte)((workingKey[x] + workingKey[y]) & 255);
            
            // XOR with keystream
            buffer[pos] ^= workingKey[tmp];
            
            // NON-STANDARD: Update y with the plaintext byte (after XOR)
            y = (y + buffer[pos]) & 255;
        }
    }
}
