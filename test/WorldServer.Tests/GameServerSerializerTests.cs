using System.Buffers;
using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization;
using Shouldly;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServer.Tests;

/// <summary>
/// Tests that EncryptKey DTOs are correctly serialized/deserialized through
/// the source-generated <see cref="GameServerContext"/> and <see cref="BinaryPacketSerializer"/>.
/// </summary>
public class GameServerSerializerTests
{
    private readonly BinaryPacketSerializer _serializer = new(new GameServerContext());

    #region EncryptKeyRequest Deserialization

    [Fact]
    public void Deserialize_EncryptKeyRequest_ReadsStructFields()
    {
        // 6-byte struct: cipher=0, app=1, major=1, minor=4, revision=8, unk1=0
        // + 256-byte key (remainder — no length prefix)
        var payload = new byte[262];
        payload[0] = 0x00; // cipher
        payload[1] = 0x01; // application
        payload[2] = 0x01; // major
        payload[3] = 0x04; // minor
        payload[4] = 0x08; // revision
        payload[5] = 0x00; // unk1
        for (var i = 6; i < 262; i++)
            payload[i] = (byte)(i & 0xFF);

        var result = _serializer.Deserialize<EncryptKeyRequest>(payload);

        result.Cipher.ShouldBe((byte)0x00);
        result.Application.ShouldBe((byte)0x01);
        result.Major.ShouldBe((byte)0x01);
        result.Minor.ShouldBe((byte)0x04);
        result.Revision.ShouldBe((byte)0x08);
        result.Unk1.ShouldBe((byte)0x00);
        result.Key.Length.ShouldBe(256);
        result.Key[0].ShouldBe((byte)0x06); // i=6 & 0xFF
    }

    [Fact]
    public void Deserialize_EncryptKeyRequest_CipherOne_ReadsKey()
    {
        var payload = new byte[262];
        payload[0] = 0x01; // cipher = RC4
        // Fill 256-byte key with 0xAA
        for (var i = 6; i < 262; i++)
            payload[i] = 0xAA;

        var result = _serializer.Deserialize<EncryptKeyRequest>(payload);

        result.Cipher.ShouldBe((byte)0x01);
        result.Key.Length.ShouldBe(256);
        result.Key.ShouldAllBe(b => b == 0xAA);
    }

    [Fact]
    public void Deserialize_EncryptKeyRequest_MinimalPayload_EmptyKey()
    {
        // Only the 6 struct bytes, no key data
        var payload = new byte[] { 0x00, 0x01, 0x01, 0x04, 0x08, 0x00 };

        var result = _serializer.Deserialize<EncryptKeyRequest>(payload);

        result.Key.Length.ShouldBe(0);
    }

    #endregion

    #region EncryptKeyResponse Serialization

    [Fact]
    public void Serialize_EncryptKeyResponse_WritesSingleByte()
    {
        var writer = new ArrayBufferWriter<byte>();
        var response = new EncryptKeyResponse { Status = 1 };

        _serializer.Serialize(writer, response);

        writer.WrittenCount.ShouldBe(1);
        writer.WrittenSpan[0].ShouldBe((byte)1);
    }

    [Fact]
    public void Serialize_EncryptKeyResponse_StatusZero()
    {
        var writer = new ArrayBufferWriter<byte>();
        var response = new EncryptKeyResponse { Status = 0 };

        _serializer.Serialize(writer, response);

        writer.WrittenCount.ShouldBe(1);
        writer.WrittenSpan[0].ShouldBe((byte)0);
    }

    #endregion

    #region Round-trip

    [Fact]
    public void EncryptKeyRequest_RoundTrips()
    {
        var original = new EncryptKeyRequest
        {
            Cipher = 1,
            Application = 2,
            Major = 3,
            Minor = 4,
            Revision = 5,
            Unk1 = 6,
            Key = [0xDE, 0xAD, 0xBE, 0xEF]
        };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        var deserialized = _serializer.Deserialize<EncryptKeyRequest>(writer.WrittenSpan);

        deserialized.Cipher.ShouldBe(original.Cipher);
        deserialized.Application.ShouldBe(original.Application);
        deserialized.Major.ShouldBe(original.Major);
        deserialized.Minor.ShouldBe(original.Minor);
        deserialized.Revision.ShouldBe(original.Revision);
        deserialized.Unk1.ShouldBe(original.Unk1);
        deserialized.Key.ShouldBe(original.Key);
    }

    #endregion
}
