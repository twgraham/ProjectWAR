using System;
using System.Buffers;
using FrameWork.NetWork.V4;
using Shouldly;

namespace FrameWork.Tests;

public class VarintLengthFramerTests
{
    private readonly VarintLengthFramer _framer = new();

    [Fact]
    public void TryExtractPacket_SinglePacketWithPayload_ExtractsCorrectly()
    {
        var data = new byte[] { 0x03, 0xAA, 0x01, 0x02, 0x03 };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();

        packet.Length.ShouldBe(4);
        packet.Span[0].ShouldBe((byte)0xAA);
        packet.Span[1].ShouldBe((byte)0x01);
        packet.Span[2].ShouldBe((byte)0x02);
        packet.Span[3].ShouldBe((byte)0x03);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_ZeroLengthPayload_ExtractsOpcodeOnly()
    {
        var data = new byte[] { 0x00, 0xFF };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();

        packet.Length.ShouldBe(1);
        packet.Span[0].ShouldBe((byte)0xFF);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_EmptyBuffer_ReturnsFalse()
    {
        var buffer = ReadOnlyMemory<byte>.Empty;

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeFalse();
        packet.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_SingleByte_ReturnsFalse()
    {
        var buffer = new ReadOnlyMemory<byte>(new byte[] { 0x05 });

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
        buffer.Length.ShouldBe(1);
    }

    [Fact]
    public void TryExtractPacket_IncompletePayload_ReturnsFalseAndRestoresBuffer()
    {
        var data = new byte[] { 0x05, 0x01, 0xAA, 0xBB };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
        buffer.Length.ShouldBe(4);
    }

    [Fact]
    public void TryExtractPacket_MultiByteVarint_ExtractsCorrectly()
    {
        var payloadSize = 200;
        var payload = new byte[payloadSize];
        for (var i = 0; i < payloadSize; i++) payload[i] = (byte)(i & 0xFF);

        var data = new byte[2 + 1 + payloadSize];
        data[0] = 0xC8;
        data[1] = 0x01;
        data[2] = 0x42;
        Array.Copy(payload, 0, data, 3, payloadSize);
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();
        packet.Length.ShouldBe(1 + payloadSize);
        packet.Span[0].ShouldBe((byte)0x42);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_MultiplePacketsInBuffer_ExtractsOneAtATime()
    {
        var data = new byte[] { 0x01, 0xAA, 0x11, 0x02, 0xBB, 0x22, 0x33 };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet1).ShouldBeTrue();
        packet1.Length.ShouldBe(2);
        packet1.Span[0].ShouldBe((byte)0xAA);
        packet1.Span[1].ShouldBe((byte)0x11);

        _framer.TryExtractPacket(ref buffer, out var packet2).ShouldBeTrue();
        packet2.Length.ShouldBe(3);
        packet2.Span[0].ShouldBe((byte)0xBB);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_ZeroLengthMissingOpcode_ReturnsFalse()
    {
        var buffer = new ReadOnlyMemory<byte>(new byte[] { 0x00 });

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryExtractPacket_NegativeVarintOverflow_ReturnsFalse()
    {
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0x01, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    [Fact]
    public void ExtractOpcode_ReturnsFirstByte()
    {
        var packet = new byte[] { 0x42, 0x01, 0x02 };

        var opcode = _framer.ExtractOpcode(packet, out var payloadOffset);

        opcode.ShouldBe((byte)0x42);
        payloadOffset.ShouldBe(1);
    }

    [Fact]
    public void ExtractOpcode_MinMaxOpcodes()
    {
        _framer.ExtractOpcode(new byte[] { 0x00 }, out _).ShouldBe((byte)0x00);
        _framer.ExtractOpcode(new byte[] { 0xFF }, out _).ShouldBe((byte)0xFF);
    }

    [Fact]
    public void CreatePacket_RoundTrips_WithTryExtractPacket()
    {
        var serializer = new BinaryPacketSerializer();
        var original = new SimplePayload { Value = 0x1234 };

        var packetBytes = _framer.CreatePacket((byte)0x10, original, serializer);
        var buffer = packetBytes;

        _framer.TryExtractPacket(ref buffer, out var extracted).ShouldBeTrue();

        var opcode = _framer.ExtractOpcode(extracted.Span, out var offset);
        opcode.ShouldBe((byte)0x10);

        var payload = extracted[offset..];
        var deserialized = serializer.Deserialize<SimplePayload>(payload.Span);
        deserialized.Value.ShouldBe((ushort)0x1234);
    }

    [Fact]
    public void CreatePacket_EmptyPayload_ProducesValidPacket()
    {
        var serializer = new BinaryPacketSerializer();
        var original = new EmptyPayload();

        var packetBytes = _framer.CreatePacket((byte)0x01, original, serializer);
        var buffer = packetBytes;

        _framer.TryExtractPacket(ref buffer, out var extracted).ShouldBeTrue();

        var opcode = _framer.ExtractOpcode(extracted.Span, out _);
        opcode.ShouldBe((byte)0x01);
    }

    public class SimplePayload
    {
        public ushort Value { get; set; }
    }

    public class EmptyPayload { }
}
