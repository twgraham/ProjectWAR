using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using FrameWork.NetWork.V4;
using Shouldly;

namespace FrameWork.Tests;

public class BinaryPacketSerializerTests
{
    private static readonly Encoding WireEncoding = Encoding.GetEncoding("iso-8859-1");
    private readonly BinaryPacketSerializer _serializer = new();

    // ──────────────────────────────────────────────
    // Primitive roundtrips
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ByteProperty()
    {
        var original = new BytePacket { Value = 0xAB };
        var result = RoundTrip<BytePacket>(original);
        result.Value.ShouldBe((byte)0xAB);
    }

    [Fact]
    public void RoundTrip_SByteProperty()
    {
        var original = new SBytePacket { Value = -42 };
        var result = RoundTrip<SBytePacket>(original);
        result.Value.ShouldBe((sbyte)-42);
    }

    [Fact]
    public void RoundTrip_Int16Property()
    {
        var original = new Int16Packet { Value = -12345 };
        var result = RoundTrip<Int16Packet>(original);
        result.Value.ShouldBe((short)-12345);
    }

    [Fact]
    public void RoundTrip_UInt16Property()
    {
        var original = new UInt16Packet { Value = 0xABCD };
        var result = RoundTrip<UInt16Packet>(original);
        result.Value.ShouldBe((ushort)0xABCD);
    }

    [Fact]
    public void RoundTrip_Int32Property()
    {
        var original = new Int32Packet { Value = -123456789 };
        var result = RoundTrip<Int32Packet>(original);
        result.Value.ShouldBe(-123456789);
    }

    [Fact]
    public void RoundTrip_UInt32Property()
    {
        var original = new UInt32Packet { Value = 0xDEADBEEF };
        var result = RoundTrip<UInt32Packet>(original);
        result.Value.ShouldBe(0xDEADBEEF);
    }

    [Fact]
    public void RoundTrip_Int64Property()
    {
        var original = new Int64Packet { Value = long.MinValue };
        var result = RoundTrip<Int64Packet>(original);
        result.Value.ShouldBe(long.MinValue);
    }

    [Fact]
    public void RoundTrip_UInt64Property()
    {
        var original = new UInt64Packet { Value = ulong.MaxValue };
        var result = RoundTrip<UInt64Packet>(original);
        result.Value.ShouldBe(ulong.MaxValue);
    }

    [Fact]
    public void RoundTrip_DoubleProperty()
    {
        var original = new DoublePacket { Value = 3.141592653589793 };
        var result = RoundTrip<DoublePacket>(original);
        result.Value.ShouldBe(3.141592653589793);
    }

    [Fact]
    public void RoundTrip_BoolProperty_True()
    {
        var original = new BoolPacket { Value = true };
        var result = RoundTrip<BoolPacket>(original);
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public void RoundTrip_BoolProperty_False()
    {
        var original = new BoolPacket { Value = false };
        var result = RoundTrip<BoolPacket>(original);
        result.Value.ShouldBeFalse();
    }

    // ──────────────────────────────────────────────
    // Float roundtrip — known bug: ReadFloat has reversed range
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FloatProperty()
    {
        var original = new FloatPacket { Value = 1.5f };
        var result = RoundTrip<FloatPacket>(original);
        result.Value.ShouldBe(1.5f);
    }

    [Fact]
    public void RoundTrip_FloatProperty_NegativeValue()
    {
        var original = new FloatPacket { Value = -3.14f };
        var result = RoundTrip<FloatPacket>(original);
        result.Value.ShouldBe(-3.14f);
    }

    [Fact]
    public void Serialize_Float_IsBigEndian()
    {
        var original = new FloatPacket { Value = 1.0f };
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        // 1.0f in IEEE 754 big-endian = 0x3F800000
        writer.WrittenSpan[0].ShouldBe((byte)0x3F);
        writer.WrittenSpan[1].ShouldBe((byte)0x80);
        writer.WrittenSpan[2].ShouldBe((byte)0x00);
        writer.WrittenSpan[3].ShouldBe((byte)0x00);
    }

    // ──────────────────────────────────────────────
    // String
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_StringProperty()
    {
        var original = new StringPacket { Name = "Hello World" };
        var result = RoundTrip<StringPacket>(original);
        result.Name.ShouldBe("Hello World");
    }

    [Fact]
    public void RoundTrip_EmptyString()
    {
        var original = new StringPacket { Name = "" };
        var result = RoundTrip<StringPacket>(original);
        result.Name.ShouldBe("");
    }

    [Fact]
    public void Serialize_NullString_IsSkipped()
    {
        // Null string is a reference type null — the serializer skips it
        var original = new StringPacket { Name = null! };
        var writer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(writer, original);

        // Null reference type property is skipped entirely (no bytes written)
        writer.WrittenCount.ShouldBe(0);
    }

    // ──────────────────────────────────────────────
    // Enums
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_EnumProperty()
    {
        var original = new EnumPacket { Status = TestStatus.Active };
        var result = RoundTrip<EnumPacket>(original);
        result.Status.ShouldBe(TestStatus.Active);
    }

    [Fact]
    public void RoundTrip_EnumProperty_ZeroValue()
    {
        var original = new EnumPacket { Status = TestStatus.None };
        var result = RoundTrip<EnumPacket>(original);
        result.Status.ShouldBe(TestStatus.None);
    }

    [Fact]
    public void Enum_SerializedAsSingleByte()
    {
        // Verify enums use exactly 1 byte on the wire
        var original = new EnumPacket { Status = TestStatus.Active };
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        writer.WrittenCount.ShouldBe(1);
        writer.WrittenSpan[0].ShouldBe((byte)TestStatus.Active);
    }

    // ──────────────────────────────────────────────
    // Arrays
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ByteArray()
    {
        var original = new ByteArrayPacket { Data = new byte[] { 0x01, 0x02, 0x03 } };
        var result = RoundTrip<ByteArrayPacket>(original);
        result.Data.ShouldBe(new byte[] { 0x01, 0x02, 0x03 });
    }

    [Fact]
    public void RoundTrip_EmptyByteArray()
    {
        var original = new ByteArrayPacket { Data = Array.Empty<byte>() };
        var result = RoundTrip<ByteArrayPacket>(original);
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_TypedArray()
    {
        var original = new UInt16ArrayPacket { Items = new ushort[] { 100, 200, 300 } };
        var result = RoundTrip<UInt16ArrayPacket>(original);
        result.Items.ShouldBe(new ushort[] { 100, 200, 300 });
    }

    [Fact]
    public void RoundTrip_EmptyTypedArray()
    {
        var original = new UInt16ArrayPacket { Items = Array.Empty<ushort>() };
        var result = RoundTrip<UInt16ArrayPacket>(original);
        result.Items.ShouldBeEmpty();
    }

    // ──────────────────────────────────────────────
    // Lists / Collections
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ListProperty()
    {
        var original = new ListPacket { Values = new List<byte> { 10, 20, 30 } };
        var result = RoundTrip<ListPacket>(original);
        result.Values.ShouldBe(new List<byte> { 10, 20, 30 });
    }

    [Fact]
    public void RoundTrip_EmptyList()
    {
        var original = new ListPacket { Values = new List<byte>() };
        var result = RoundTrip<ListPacket>(original);
        result.Values.ShouldBeEmpty();
    }

    // ──────────────────────────────────────────────
    // Multiple properties
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_MultipleProperties_PreservesOrder()
    {
        var original = new MultiPropertyPacket
        {
            Id = 0xAB,
            Count = 0x1234,
            Name = "test"
        };
        var result = RoundTrip<MultiPropertyPacket>(original);
        result.Id.ShouldBe((byte)0xAB);
        result.Count.ShouldBe((ushort)0x1234);
        result.Name.ShouldBe("test");
    }

    // ──────────────────────────────────────────────
    // Nullable properties
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_NullableProperty_WithValue()
    {
        var original = new NullablePacket { MaybeId = 42 };
        var result = RoundTrip<NullablePacket>(original);
        result.MaybeId.ShouldBe(42);
    }

    [Fact]
    public void Serialize_NullableProperty_NullValue_SkipsProperty()
    {
        // When nullable property is null, serializer skips it
        var original = new NullablePacket { MaybeId = null };
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        // No bytes written (the null property is skipped)
        writer.WrittenCount.ShouldBe(0);
    }

    [Fact]
    public void Deserialize_NullableProperty_AtEndOfBuffer_SetsNull()
    {
        // When buffer is exhausted and property is nullable, it should be set to null
        var empty = ReadOnlySpan<byte>.Empty;
        var result = _serializer.Deserialize<NullablePacket>(empty);
        result.MaybeId.ShouldBeNull();
    }

    // ──────────────────────────────────────────────
    // Nullable reference type (string?) at end of buffer
    // ──────────────────────────────────────────────

    [Fact]
    public void Deserialize_TrailingNullableString_AtEndOfBuffer_SetsNull()
    {
        // Packet with required byte + nullable string. Supply only the byte.
        var data = new byte[] { 0x42 };
        var result = _serializer.Deserialize<TrailingNullablePacket>(data);
        result.Id.ShouldBe((byte)0x42);
        result.OptionalName.ShouldBeNull();
    }

    // ──────────────────────────────────────────────
    // PacketLength attribute
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_PacketLengthAttribute_TwoByte()
    {
        var original = new TwoByteArrayLengthPacket { Data = new byte[] { 1, 2, 3, 4, 5 } };
        var result = RoundTrip<TwoByteArrayLengthPacket>(original);
        result.Data.ShouldBe(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void RoundTrip_PacketLengthAttribute_FourByte()
    {
        var original = new FourByteArrayLengthPacket { Data = new byte[] { 10, 20, 30 } };
        var result = RoundTrip<FourByteArrayLengthPacket>(original);
        result.Data.ShouldBe(new byte[] { 10, 20, 30 });
    }

    // ──────────────────────────────────────────────
    // Value type rejection
    // ──────────────────────────────────────────────

    [Fact]
    public void Deserialize_ValueType_ThrowsInvalidOperation()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        Should.Throw<InvalidOperationException>(() =>
        {
            _serializer.Deserialize<int>(data);
        });
    }

    [Fact]
    public void Serialize_ValueType_ThrowsInvalidOperation()
    {
        var writer = new ArrayBufferWriter<byte>();
        Should.Throw<InvalidOperationException>(() =>
        {
            _serializer.Serialize(writer, 42);
        });
    }

    // ──────────────────────────────────────────────
    // Big-endian wire format verification
    // ──────────────────────────────────────────────

    [Fact]
    public void Serialize_Int16_IsBigEndian()
    {
        var original = new Int16Packet { Value = 0x0102 };
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        writer.WrittenSpan[0].ShouldBe((byte)0x01);
        writer.WrittenSpan[1].ShouldBe((byte)0x02);
    }

    [Fact]
    public void Serialize_Int32_IsBigEndian()
    {
        var original = new Int32Packet { Value = 0x01020304 };
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        writer.WrittenSpan[0].ShouldBe((byte)0x01);
        writer.WrittenSpan[1].ShouldBe((byte)0x02);
        writer.WrittenSpan[2].ShouldBe((byte)0x03);
        writer.WrittenSpan[3].ShouldBe((byte)0x04);
    }

    [Fact]
    public void Serialize_String_HasBigEndianLengthPrefix()
    {
        var original = new StringPacket { Name = "AB" }; // 2 bytes in iso-8859-1
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);

        // 4-byte BE length = 2, then "AB"
        writer.WrittenCount.ShouldBe(6);
        BinaryPrimitives.ReadUInt32BigEndian(writer.WrittenSpan[..4]).ShouldBe(2u);
        writer.WrittenSpan[4].ShouldBe((byte)'A');
        writer.WrittenSpan[5].ShouldBe((byte)'B');
    }

    // ──────────────────────────────────────────────
    // Unsupported type
    // ──────────────────────────────────────────────

    [Fact]
    public void Deserialize_UnsupportedPropertyType_Throws()
    {
        // Dictionary<string, string> is not a supported generic type
        var writer = new ArrayBufferWriter<byte>();
        // Write some dummy data
        var original = new UnsupportedTypePacket();
        Should.Throw<NotSupportedException>(() =>
        {
            _serializer.Serialize(writer, original);
        });
    }

    // ──────────────────────────────────────────────
    // Empty payload → empty object
    // ──────────────────────────────────────────────

    [Fact]
    public void Deserialize_EmptyPayload_EmptyObject()
    {
        var result = _serializer.Deserialize<EmptyPacket>(ReadOnlySpan<byte>.Empty);
        result.ShouldNotBeNull();
    }

    // ──────────────────────────────────────────────
    // Read-only properties are skipped
    // ──────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ReadOnlyProperties_AreSkipped()
    {
        var original = new ReadOnlyPropertyPacket { Writable = 0x42 };
        var result = RoundTrip<ReadOnlyPropertyPacket>(original);
        result.Writable.ShouldBe((byte)0x42);
        result.ReadOnly.ShouldBe(0); // default, not serialized
    }

    // ──────────────────────────────────────────────
    // BinaryPacketSerializerFactory
    // ──────────────────────────────────────────────

    [Fact]
    public void Factory_CreateReturnsSameInstance()
    {
        var factory = new BinaryPacketSerializerFactory();
        var a = factory.Create();
        var b = factory.Create();
        a.ShouldBeSameAs(b);
    }

    // ──────────────────────────────────────────────
    // Helper
    // ──────────────────────────────────────────────

    private T RoundTrip<T>(T original)
    {
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        return _serializer.Deserialize<T>(writer.WrittenSpan);
    }

    // ──────────────────────────────────────────────
    // Test types
    // ──────────────────────────────────────────────

    public class BytePacket { public byte Value { get; set; } }
    public class SBytePacket { public sbyte Value { get; set; } }
    public class Int16Packet { public short Value { get; set; } }
    public class UInt16Packet { public ushort Value { get; set; } }
    public class Int32Packet { public int Value { get; set; } }
    public class UInt32Packet { public uint Value { get; set; } }
    public class Int64Packet { public long Value { get; set; } }
    public class UInt64Packet { public ulong Value { get; set; } }
    public class FloatPacket { public float Value { get; set; } }
    public class DoublePacket { public double Value { get; set; } }
    public class BoolPacket { public bool Value { get; set; } }
    public class StringPacket { public string Name { get; set; } = "";  }
    public class EnumPacket { public TestStatus Status { get; set; } }
    public class ByteArrayPacket { [PacketLength(4)] public byte[] Data { get; set; } = Array.Empty<byte>(); }
    public class UInt16ArrayPacket { public ushort[] Items { get; set; } = Array.Empty<ushort>(); }
    public class ListPacket { public List<byte> Values { get; set; } = new(); }
    public class MultiPropertyPacket { public byte Id { get; set; } public ushort Count { get; set; } public string Name { get; set; } = ""; }
    public class NullablePacket { public int? MaybeId { get; set; } }
    public class TrailingNullablePacket { public byte Id { get; set; } public string? OptionalName { get; set; } }
    public class EmptyPacket { }
    public class ReadOnlyPropertyPacket { public byte Writable { get; set; } public int ReadOnly => 0; }

    public class TwoByteArrayLengthPacket
    {
        [PacketLength(2)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class FourByteArrayLengthPacket
    {
        [PacketLength(4)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class UnsupportedTypePacket
    {
        public Dictionary<string, string> Map { get; set; } = new();
    }

    public enum TestStatus : byte
    {
        None = 0,
        Active = 1,
        Inactive = 2
    }
}
