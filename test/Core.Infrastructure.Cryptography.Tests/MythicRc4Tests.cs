namespace Core.Infrastructure.Cryptography.Tests;

public class MythicRc4Tests
{
    [Fact]
    public void EncryptDecrypt_RoundTrip_ProducesOriginalData()
    {
        // Arrange
        var key = new byte[256];
        var random = new Random(42); // Fixed seed for reproducibility
        random.NextBytes(key);

        var originalData = new byte[100];
        random.NextBytes(originalData);

        var data = (byte[])originalData.Clone();

        // Act
        MythicRc4.Encrypt(key, data, 0, data.Length);
        MythicRc4.Decrypt(key, data, 0, data.Length);

        // Assert
        Assert.Equal(originalData, data);
    }

    [Fact]
    public void Encrypt_WithOffset_WorksCorrectly()
    {
        // Arrange
        var key = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            key[i] = (byte)i;
        }

        var testData = new byte[] { 
            0x00, 0x00, 0x00, 0x00, // Padding
            0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x20, 0x57, 0x6F, 
            0x72, 0x6C, 0x64, 0x21,
            0x00, 0x00, 0x00, 0x00  // Padding
        };

        var originalPadding = new[] { testData[0], testData[1], testData[2], testData[3], testData[16], testData[17], testData[18], testData[19] };
        var encryptedData = (byte[])testData.Clone();

        var offset = 4;
        var length = 12;

        // Act
        MythicRc4.Encrypt(key, encryptedData, offset, length);

        // Assert - padding unchanged
        Assert.Equal(originalPadding[0], encryptedData[0]);
        Assert.Equal(originalPadding[1], encryptedData[1]);
        Assert.Equal(originalPadding[2], encryptedData[2]);
        Assert.Equal(originalPadding[3], encryptedData[3]);
        Assert.Equal(originalPadding[4], encryptedData[16]);
        Assert.Equal(originalPadding[5], encryptedData[17]);
        Assert.Equal(originalPadding[6], encryptedData[18]);
        Assert.Equal(originalPadding[7], encryptedData[19]);
        
        // Data changed
        Assert.NotEqual(testData[4], encryptedData[4]);
        
        // Round trip works
        MythicRc4.Decrypt(key, encryptedData, offset, length);
        Assert.Equal(testData, encryptedData);
    }

    [Fact]
    public void Decrypt_WithOffset_WorksCorrectly()
    {
        // Arrange
        var key = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            key[i] = (byte)(i * 2 % 256);
        }

        var originalData = new byte[] { 
            0xFF, 0xFF, // Padding
            0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
            0xFF, 0xFF  // Padding
        };

        var testData = (byte[])originalData.Clone();

        var offset = 2;
        var length = 8;

        // Act - encrypt then decrypt
        MythicRc4.Encrypt(key, testData, offset, length);
        MythicRc4.Decrypt(key, testData, offset, length);

        // Assert
        Assert.Equal(originalData, testData);
        // Verify padding wasn't touched
        Assert.Equal<byte>(0xFF, testData[0]);
        Assert.Equal<byte>(0xFF, testData[10]);
    }

    [Fact]
    public void Encrypt_LargeData_RoundTripWorks()
    {
        // Arrange
        var key = new byte[256];
        var random = new Random(123);
        random.NextBytes(key);

        var originalData = new byte[1024];
        random.NextBytes(originalData);

        var testData = (byte[])originalData.Clone();

        // Act
        MythicRc4.Encrypt(key, testData, 0, testData.Length);
        
        // Data should be different after encryption
        Assert.NotEqual(originalData, testData);
        
        MythicRc4.Decrypt(key, testData, 0, testData.Length);

        // Assert - round trip produces original
        Assert.Equal(originalData, testData);
    }

    [Fact]
    public void Decrypt_LargeData_RoundTripWorks()
    {
        // Arrange
        var key = new byte[256];
        var random = new Random(456);
        random.NextBytes(key);

        var originalData = new byte[2048];
        random.NextBytes(originalData);

        var testData = (byte[])originalData.Clone();

        // Act - encrypt then decrypt large data
        MythicRc4.Encrypt(key, testData, 0, testData.Length);
        MythicRc4.Decrypt(key, testData, 0, testData.Length);

        // Assert
        Assert.Equal(originalData, testData);
    }

    [Fact]
    public void Encrypt_OddLength_RoundTripWorks()
    {
        // Arrange
        var key = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            key[i] = (byte)((i * 3) % 256);
        }

        // Odd length to test midpoint calculation
        var originalData = new byte[17];
        for (var i = 0; i < originalData.Length; i++)
        {
            originalData[i] = (byte)(i + 10);
        }

        var testData = (byte[])originalData.Clone();

        // Act
        MythicRc4.Encrypt(key, testData, 0, testData.Length);
        MythicRc4.Decrypt(key, testData, 0, testData.Length);

        // Assert
        Assert.Equal(originalData, testData);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentCiphertext()
    {
        // Arrange
        var key1 = new byte[256];
        var key2 = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            key1[i] = (byte)i;
            key2[i] = (byte)(255 - i);
        }

        var plaintext = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var cipher1 = (byte[])plaintext.Clone();
        var cipher2 = (byte[])plaintext.Clone();

        // Act
        MythicRc4.Encrypt(key1, cipher1, 0, cipher1.Length);
        MythicRc4.Encrypt(key2, cipher2, 0, cipher2.Length);

        // Assert
        Assert.NotEqual(cipher1, cipher2);
    }

    [Fact]
    public void ZeroLengthData_DoesNotThrow()
    {
        // Arrange
        var key = new byte[256];
        var data = Array.Empty<byte>();

        // Act & Assert - should not throw
        MythicRc4.Encrypt(key, data, 0, 0);
        MythicRc4.Decrypt(key, data, 0, 0);
    }
}
