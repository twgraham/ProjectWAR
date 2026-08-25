using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Core.Infrastructure.Network.Serialization;
using Core.Infrastructure.Network.Serialization.Attributes;
using Shouldly;

namespace Core.Infrastructure.Network.Tests;

public class BinaryPacketSerializerTests
{
    private static readonly Encoding WireEncoding = Encoding.GetEncoding("iso-8859-1");
    private readonly BinaryPacketSerializer _serializer = new();

    [Fact]
    public void RoundTrip_ByteProperty()
    {
        // GIVEN: A packet with a byte property set to 0xAB
        var original = new BytePacket { Value = 0xAB };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<BytePacket>(original);

        // THEN: The byte value is preserved correctly
        result.Value.ShouldBe((byte)0xAB);
    }

    [Fact]
    public void RoundTrip_SByteProperty()
    {
        // GIVEN: A packet with a signed byte property set to -42
        var original = new SBytePacket { Value = -42 };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<SBytePacket>(original);

        // THEN: The signed byte value is preserved correctly
        result.Value.ShouldBe((sbyte)-42);
    }

    [Fact]
    public void RoundTrip_Int16Property()
    {
        // GIVEN: A packet with a 16-bit signed integer set to -12345
        var original = new Int16Packet { Value = -12345 };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<Int16Packet>(original);

        // THEN: The Int16 value is preserved correctly
        result.Value.ShouldBe((short)-12345);
    }

    [Fact]
    public void RoundTrip_UInt16Property()
    {
        // GIVEN: A packet with a 16-bit unsigned integer set to 0xABCD
        var original = new UInt16Packet { Value = 0xABCD };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<UInt16Packet>(original);

        // THEN: The UInt16 value is preserved correctly
        result.Value.ShouldBe((ushort)0xABCD);
    }

    [Fact]
    public void RoundTrip_Int32Property()
    {
        // GIVEN: A packet with a 32-bit signed integer set to -123456789
        var original = new Int32Packet { Value = -123456789 };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<Int32Packet>(original);

        // THEN: The Int32 value is preserved correctly
        result.Value.ShouldBe(-123456789);
    }

    [Fact]
    public void RoundTrip_UInt32Property()
    {
        // GIVEN: A packet with a 32-bit unsigned integer set to 0xDEADBEEF
        var original = new UInt32Packet { Value = 0xDEADBEEF };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<UInt32Packet>(original);

        // THEN: The UInt32 value is preserved correctly
        result.Value.ShouldBe(0xDEADBEEF);
    }

    [Fact]
    public void RoundTrip_Int64Property()
    {
        // GIVEN: A packet with a 64-bit signed integer set to long.MinValue
        var original = new Int64Packet { Value = long.MinValue };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<Int64Packet>(original);

        // THEN: The Int64 value is preserved correctly
        result.Value.ShouldBe(long.MinValue);
    }

    [Fact]
    public void RoundTrip_UInt64Property()
    {
        // GIVEN: A packet with a 64-bit unsigned integer set to ulong.MaxValue
        var original = new UInt64Packet { Value = ulong.MaxValue };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<UInt64Packet>(original);

        // THEN: The UInt64 value is preserved correctly
        result.Value.ShouldBe(ulong.MaxValue);
    }

    [Fact]
    public void RoundTrip_DoubleProperty()
    {
        // GIVEN: A packet with a double-precision floating point value of Pi
        var original = new DoublePacket { Value = 3.141592653589793 };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<DoublePacket>(original);

        // THEN: The double value is preserved with full precision
        result.Value.ShouldBe(3.141592653589793);
    }

    [Fact]
    public void RoundTrip_BoolProperty_True()
    {
        // GIVEN: A packet with a boolean property set to true
        var original = new BoolPacket { Value = true };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<BoolPacket>(original);

        // THEN: The boolean true value is preserved correctly
        result.Value.ShouldBeTrue();
    }

    [Fact]
    public void RoundTrip_BoolProperty_False()
    {
        // GIVEN: A packet with a boolean property set to false
        var original = new BoolPacket { Value = false };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<BoolPacket>(original);

        // THEN: The boolean false value is preserved correctly
        result.Value.ShouldBeFalse();
    }

    [Fact]
    public void RoundTrip_FloatProperty()
    {
        // GIVEN: A packet with a single-precision float set to 1.5
        var original = new FloatPacket { Value = 1.5f };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<FloatPacket>(original);

        // THEN: The float value is preserved correctly
        result.Value.ShouldBe(1.5f);
    }

    [Fact]
    public void RoundTrip_FloatProperty_NegativeValue()
    {
        // GIVEN: A packet with a negative float value of -3.14
        var original = new FloatPacket { Value = -3.14f };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<FloatPacket>(original);

        // THEN: The negative float value is preserved correctly
        result.Value.ShouldBe(-3.14f);
    }

    [Fact]
    public void Serialize_Float_IsBigEndian()
    {
        // GIVEN: A packet with a float value of 1.0
        var original = new FloatPacket { Value = 1.0f };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The float is written in big-endian byte order (0x3F800000)
        writer.WrittenSpan[0].ShouldBe((byte)0x3F);
        writer.WrittenSpan[1].ShouldBe((byte)0x80);
        writer.WrittenSpan[2].ShouldBe((byte)0x00);
        writer.WrittenSpan[3].ShouldBe((byte)0x00);
    }

    [Fact]
    public void RoundTrip_StringProperty()
    {
        // GIVEN: A packet with a string property set to "Hello World"
        var original = new StringPacket { Name = "Hello World" };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<StringPacket>(original);

        // THEN: The string value is preserved correctly
        result.Name.ShouldBe("Hello World");
    }

    [Fact]
    public void RoundTrip_EmptyString()
    {
        // GIVEN: A packet with an empty string property
        var original = new StringPacket { Name = "" };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<StringPacket>(original);

        // THEN: The empty string is preserved correctly
        result.Name.ShouldBe("");
    }

    [Fact]
    public void Serialize_NullString_IsSkipped()
    {
        // GIVEN: A packet with a null string property
        var original = new StringPacket { Name = null! };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The null string is skipped and no bytes are written
        writer.WrittenCount.ShouldBe(0);
    }

    [Fact]
    public void RoundTrip_EnumProperty()
    {
        // GIVEN: A packet with an enum property set to TestStatus.Active
        var original = new EnumPacket { Status = TestStatus.Active };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<EnumPacket>(original);

        // THEN: The enum value is preserved correctly
        result.Status.ShouldBe(TestStatus.Active);
    }

    [Fact]
    public void RoundTrip_EnumProperty_ZeroValue()
    {
        // GIVEN: A packet with an enum property set to TestStatus.None (zero value)
        var original = new EnumPacket { Status = TestStatus.None };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<EnumPacket>(original);

        // THEN: The zero-value enum is preserved correctly
        result.Status.ShouldBe(TestStatus.None);
    }

    [Fact]
    public void Enum_SerializedAsSingleByte()
    {
        // GIVEN: A packet with an enum property set to TestStatus.Active
        var original = new EnumPacket { Status = TestStatus.Active };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The enum is serialized as a single byte with its underlying value
        writer.WrittenCount.ShouldBe(1);
        writer.WrittenSpan[0].ShouldBe((byte)TestStatus.Active);
    }

    [Fact]
    public void RoundTrip_ByteArray()
    {
        // GIVEN: A packet with a byte array containing three elements
        var original = new ByteArrayPacket { Data = new byte[] { 0x01, 0x02, 0x03 } };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<ByteArrayPacket>(original);

        // THEN: The byte array is preserved correctly
        result.Data.ShouldBe(new byte[] { 0x01, 0x02, 0x03 });
    }

    [Fact]
    public void RoundTrip_EmptyByteArray()
    {
        // GIVEN: A packet with an empty byte array
        var original = new ByteArrayPacket { Data = Array.Empty<byte>() };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<ByteArrayPacket>(original);

        // THEN: The empty array is preserved correctly
        result.Data.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_TypedArray()
    {
        // GIVEN: A packet with a ushort array containing three elements
        var original = new UInt16ArrayPacket { Items = new ushort[] { 100, 200, 300 } };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<UInt16ArrayPacket>(original);

        // THEN: The ushort array is preserved correctly
        result.Items.ShouldBe(new ushort[] { 100, 200, 300 });
    }

    [Fact]
    public void RoundTrip_EmptyTypedArray()
    {
        // GIVEN: A packet with an empty ushort array
        var original = new UInt16ArrayPacket { Items = Array.Empty<ushort>() };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<UInt16ArrayPacket>(original);

        // THEN: The empty typed array is preserved correctly
        result.Items.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_ListProperty()
    {
        // GIVEN: A packet with a List<byte> containing three elements
        var original = new ListPacket { Values = new List<byte> { 10, 20, 30 } };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<ListPacket>(original);

        // THEN: The list contents are preserved correctly
        result.Values.ShouldBe(new List<byte> { 10, 20, 30 });
    }

    [Fact]
    public void RoundTrip_EmptyList()
    {
        // GIVEN: A packet with an empty List<byte>
        var original = new ListPacket { Values = new List<byte>() };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<ListPacket>(original);

        // THEN: The empty list is preserved correctly
        result.Values.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_IEnumerableProperty()
    {
        // GIVEN: A packet with an IEnumerable<int> property set to three elements
        var original = new IEnumerablePacket { Values = new[] { 1, 2, 3 } };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<IEnumerablePacket>(original);

        // THEN: Elements are preserved (deserialized back as T[], which satisfies IEnumerable<int>)
        result.Values.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public void RoundTrip_IEnumerableProperty_Empty()
    {
        // GIVEN: A packet with an empty IEnumerable<int>
        var original = new IEnumerablePacket { Values = Array.Empty<int>() };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<IEnumerablePacket>(original);

        // THEN: Empty sequence is preserved
        result.Values.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_MultipleProperties_PreservesOrder()
    {
        var original = new MultiPropertyPacket
        {
            Id = 0xAB,
            Count = 0x1234,
            Name = "test"
        };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<MultiPropertyPacket>(original);

        // THEN: All properties are preserved with correct values in order
        result.Id.ShouldBe((byte)0xAB);
        result.Count.ShouldBe((ushort)0x1234);
        result.Name.ShouldBe("test");
    }

    [Fact]
    public void RoundTrip_NullableProperty_WithValue()
    {
        // GIVEN: A packet with a nullable int property set to 42
        var original = new NullablePacket { MaybeId = 42 };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<NullablePacket>(original);

        // THEN: The nullable value is preserved correctly
        result.MaybeId.ShouldBe(42);
    }

    [Fact]
    public void Serialize_NullableProperty_NullValue_SkipsProperty()
    {
        // GIVEN: A packet with a nullable int property set to null
        var original = new NullablePacket { MaybeId = null };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The null property is skipped and no bytes are written
        writer.WrittenCount.ShouldBe(0);
    }

    [Fact]
    public void Deserialize_NullableProperty_AtEndOfBuffer_SetsNull()
    {
        // GIVEN: An empty buffer with no data for nullable property
        var empty = ReadOnlySpan<byte>.Empty;

        // WHEN: Deserializing the packet
        var result = _serializer.Deserialize<NullablePacket>(empty);

        // THEN: The nullable property is set to null
        result.MaybeId.ShouldBeNull();
    }

    [Fact]
    public void Deserialize_TrailingNullableString_AtEndOfBuffer_SetsNull()
    {
        // GIVEN: A buffer with only the Id byte, no data for trailing nullable string
        var data = new byte[] { 0x42 };

        // WHEN: Deserializing the packet
        var result = _serializer.Deserialize<TrailingNullablePacket>(data);

        // THEN: The Id is read and the trailing nullable string is set to null
        result.Id.ShouldBe((byte)0x42);
        result.OptionalName.ShouldBeNull();
    }

    [Fact]
    public void RoundTrip_PacketLengthAttribute_TwoByte()
    {
        // GIVEN: A packet with byte array using 2-byte length prefix attribute
        var original = new TwoByteArrayLengthPacket { Data = new byte[] { 1, 2, 3, 4, 5 } };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<TwoByteArrayLengthPacket>(original);

        // THEN: The array is correctly serialized with 2-byte length and preserved
        result.Data.ShouldBe(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void RoundTrip_PacketLengthAttribute_FourByte()
    {
        // GIVEN: A packet with byte array using 4-byte length prefix attribute
        var original = new FourByteArrayLengthPacket { Data = new byte[] { 10, 20, 30 } };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<FourByteArrayLengthPacket>(original);

        // THEN: The array is correctly serialized with 4-byte length and preserved
        result.Data.ShouldBe(new byte[] { 10, 20, 30 });
    }

    [Fact]
    public void Deserialize_ValueType_ThrowsInvalidOperation()
    {
        // GIVEN: A byte buffer and an attempt to deserialize to a primitive value type
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };

        // WHEN: Attempting to deserialize to int (a value type)
        // THEN: An InvalidOperationException is thrown
        Should.Throw<InvalidOperationException>(() =>
        {
            _serializer.Deserialize<int>(data);
        });
    }

    [Fact]
    public void Serialize_ValueType_ThrowsInvalidOperation()
    {
        // GIVEN: A primitive value type (int) and an attempt to serialize it
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Attempting to serialize an int value type
        // THEN: An InvalidOperationException is thrown
        Should.Throw<InvalidOperationException>(() =>
        {
            _serializer.Serialize(writer, 42);
        });
    }

    [Fact]
    public void Serialize_Int16_IsBigEndian()
    {
        // GIVEN: A packet with Int16 value 0x0102
        var original = new Int16Packet { Value = 0x0102 };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The Int16 is written in big-endian byte order (0x01 0x02)
        writer.WrittenSpan[0].ShouldBe((byte)0x01);
        writer.WrittenSpan[1].ShouldBe((byte)0x02);
    }

    [Fact]
    public void Serialize_Int32_IsBigEndian()
    {
        // GIVEN: A packet with Int32 value 0x01020304
        var original = new Int32Packet { Value = 0x01020304 };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The Int32 is written in big-endian byte order (0x01 0x02 0x03 0x04)
        writer.WrittenSpan[0].ShouldBe((byte)0x01);
        writer.WrittenSpan[1].ShouldBe((byte)0x02);
        writer.WrittenSpan[2].ShouldBe((byte)0x03);
        writer.WrittenSpan[3].ShouldBe((byte)0x04);
    }

    [Fact]
    public void Serialize_String_HasBigEndianLengthPrefix()
    {
        // GIVEN: A packet with a string property "AB" (2 characters)
        var original = new StringPacket { Name = "AB" };
        var writer = new ArrayBufferWriter<byte>();

        // WHEN: Serializing the packet
        _serializer.Serialize(writer, original);

        // THEN: The string is prefixed with 4-byte big-endian length (2) followed by string bytes
        writer.WrittenCount.ShouldBe(6);
        BinaryPrimitives.ReadUInt32BigEndian(writer.WrittenSpan[..4]).ShouldBe(2u);
        writer.WrittenSpan[4].ShouldBe((byte)'A');
        writer.WrittenSpan[5].ShouldBe((byte)'B');
    }

    [Fact]
    public void Deserialize_UnsupportedPropertyType_Throws()
    {
        // GIVEN: A packet with an unsupported property type (Dictionary)
        var writer = new ArrayBufferWriter<byte>();
        var original = new UnsupportedTypePacket();

        // WHEN: Attempting to serialize the packet
        // THEN: A NotSupportedException is thrown for the unsupported type
        Should.Throw<NotSupportedException>(() =>
        {
            _serializer.Serialize(writer, original);
        });
    }

    [Fact]
    public void Deserialize_EmptyPayload_EmptyObject()
    {
        // GIVEN: An empty buffer and a packet type with no properties
        // WHEN: Deserializing the empty buffer
        var result = _serializer.Deserialize<EmptyPacket>(ReadOnlySpan<byte>.Empty);

        // THEN: A valid empty packet object is created
        result.ShouldNotBeNull();
    }

    [Fact]
    public void RoundTrip_ReadOnlyProperties_AreSkipped()
    {
        // GIVEN: A packet with both writable and read-only properties
        var original = new ReadOnlyPropertyPacket { Writable = 0x42 };

        // WHEN: Serializing and deserializing the packet
        var result = RoundTrip<ReadOnlyPropertyPacket>(original);

        // THEN: Writable property is preserved, read-only property is skipped and has default value
        result.Writable.ShouldBe((byte)0x42);
        result.ReadOnly.ShouldBe(0);
    }

    private T RoundTrip<T>(T original)
    {
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        return _serializer.Deserialize<T>(writer.WrittenSpan);
    }

    [Fact]
    public void RoundTrip_FixedLengthAttribute_ExactLength()
    {
        // GIVEN: A packet with a 4-byte fixed field set to exactly 4 bytes
        var original = new FixedLengthByteArrayPacket { Data = new byte[] { 0x01, 0x02, 0x03, 0x04 } };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<FixedLengthByteArrayPacket>(original);

        // THEN: All four bytes are preserved and no length prefix byte is in the wire format
        result.Data.ShouldBe(new byte[] { 0x01, 0x02, 0x03, 0x04 });

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(4); // exactly 4 bytes, no length prefix
    }

    [Fact]
    public void RoundTrip_FixedLengthAttribute_ShorterArrayIsPadded()
    {
        // GIVEN: A packet with a 4-byte fixed field set to only 2 bytes
        var original = new FixedLengthByteArrayPacket { Data = new byte[] { 0xAA, 0xBB } };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<FixedLengthByteArrayPacket>(original);

        // THEN: The 2 content bytes are followed by 2 zero-padding bytes
        result.Data.ShouldBe(new byte[] { 0xAA, 0xBB, 0x00, 0x00 });
    }

    [Fact]
    public void RoundTrip_FixedLengthAttribute_LongerArrayIsTruncated()
    {
        // GIVEN: A packet with a 4-byte fixed field set to 6 bytes
        var original = new FixedLengthByteArrayPacket { Data = new byte[] { 1, 2, 3, 4, 5, 6 } };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<FixedLengthByteArrayPacket>(original);

        // THEN: Only the first 4 bytes are preserved
        result.Data.ShouldBe(new byte[] { 1, 2, 3, 4 });
    }

    [Fact]
    public void RoundTrip_FixedLengthAttribute_WithNeighbouringProperties()
    {
        // GIVEN: A packet where a fixed byte field sits between two other properties
        var original = new FixedLengthWithNeighboursPacket
        {
            Before = 0x11,
            Fixed = new byte[] { 0xAA, 0xBB, 0xCC },
            After = 0x22,
        };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<FixedLengthWithNeighboursPacket>(original);

        // THEN: All values are correctly preserved with a total wire size of 5 bytes
        result.Before.ShouldBe((byte)0x11);
        result.Fixed.ShouldBe(new byte[] { 0xAA, 0xBB, 0xCC });
        result.After.ShouldBe((byte)0x22);

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(5); // 1 + 3 + 1
    }

    [Fact]
    public void RoundTrip_PascalString_NormalString()
    {
        // GIVEN: A packet with a Pascal string property
        var original = new PascalStringPacket { Name = "Hello" };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<PascalStringPacket>(original);

        // THEN: The string is preserved and the wire format is [length:1][bytes:N] with no null terminator
        result.Name.ShouldBe("Hello");

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(1 + 5); // 1-byte length + 5 bytes for "Hello"
        writer.WrittenSpan[0].ShouldBe((byte)5); // length byte
    }

    [Fact]
    public void RoundTrip_PascalString_EmptyString()
    {
        // GIVEN: A packet with an empty Pascal string
        var original = new PascalStringPacket { Name = "" };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<PascalStringPacket>(original);

        // THEN: An empty string round-trips correctly as a single zero byte
        result.Name.ShouldBe("");

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(1);   // just the length byte 0x00
        writer.WrittenSpan[0].ShouldBe((byte)0);
    }

    [Fact]
    public void RoundTrip_PascalString_WithNeighbouringProperties()
    {
        // GIVEN: A Pascal string field flanked by two byte properties
        var original = new PascalStringWithNeighboursPacket
        {
            Before = 0xAA,
            Text = "Hi",
            After = 0xBB,
        };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<PascalStringWithNeighboursPacket>(original);

        // THEN: All three fields are correctly preserved
        result.Before.ShouldBe((byte)0xAA);
        result.Text.ShouldBe("Hi");
        result.After.ShouldBe((byte)0xBB);

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(1 + 1 + 2 + 1); // Before(1) + length(1) + "Hi"(2) + After(1)
    }

    [Fact]
    public void RoundTrip_LittleEndian_Int32()
    {
        // GIVEN: A packet whose int is tagged with [LittleEndian]
        var original = new LittleEndianInt32Packet { Value = 0x01020304 };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<LittleEndianInt32Packet>(original);

        // THEN: Value round-trips correctly
        result.Value.ShouldBe(0x01020304);

        // AND: The wire bytes are in little-endian order (LSB first)
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenSpan[0].ShouldBe((byte)0x04); // LSB
        writer.WrittenSpan[1].ShouldBe((byte)0x03);
        writer.WrittenSpan[2].ShouldBe((byte)0x02);
        writer.WrittenSpan[3].ShouldBe((byte)0x01); // MSB
    }

    [Fact]
    public void RoundTrip_LittleEndian_UInt16()
    {
        // GIVEN: A packet whose ushort is tagged with [LittleEndian]
        var original = new LittleEndianUInt16Packet { Value = 0xABCD };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<LittleEndianUInt16Packet>(original);

        // THEN: Value round-trips correctly
        result.Value.ShouldBe((ushort)0xABCD);

        // AND: The wire bytes are in little-endian order (LSB first)
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenSpan[0].ShouldBe((byte)0xCD); // LSB
        writer.WrittenSpan[1].ShouldBe((byte)0xAB); // MSB
    }

    [Fact]
    public void RoundTrip_LittleEndian_DoesNotAffectBigEndianSibling()
    {
        // GIVEN: A packet with one big-endian and one little-endian int field
        var original = new LittleEndianMixedPacket { BigEndian = 0x11223344, LittleEndian = 0x55667788 };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<LittleEndianMixedPacket>(original);

        // THEN: Both values round-trip correctly
        result.BigEndian.ShouldBe(0x11223344);
        result.LittleEndian.ShouldBe(0x55667788);

        // AND: First 4 bytes are big-endian, next 4 are little-endian
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenSpan[0].ShouldBe((byte)0x11); // BigEndian MSB first
        writer.WrittenSpan[4].ShouldBe((byte)0x88); // LittleEndian LSB first
    }

    [Fact]
    public void RoundTrip_FixedLength_TypedArray()
    {
        // GIVEN: A packet with [FixedLength(3)] int[]
        var original = new FixedLengthTypedArrayPacket { Values = new[] { 1, 2, 3 } };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<FixedLengthTypedArrayPacket>(original);

        // THEN: Values are preserved
        result.Values.ShouldBe(new[] { 1, 2, 3 });

        // AND: No length prefix written — wire size is exactly 3 * 4 = 12 bytes
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(12);
    }

    [Fact]
    public void RoundTrip_FixedLength_TypedArray_WithNeighbours()
    {
        // GIVEN: A packet with surrounding bytes flanking a [FixedLength(2)] ushort[]
        var original = new FixedLengthTypedArrayWithNeighboursPacket
            { Before = 0xAA, Items = new ushort[] { 0x0102, 0x0304 }, After = 0xBB };

        // WHEN
        var result = RoundTrip<FixedLengthTypedArrayWithNeighboursPacket>(original);

        // THEN: Surrounding bytes + array values intact
        result.Before.ShouldBe((byte)0xAA);
        result.Items.ShouldBe(new ushort[] { 0x0102, 0x0304 });
        result.After.ShouldBe((byte)0xBB);

        // AND: Wire size = 1 + 2*2 + 1 = 6 bytes (no length prefix)
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(6);
    }

    [Fact]
    public void RoundTrip_FixedLength_List()
    {
        // GIVEN: A packet with [FixedLength(3)] List<int>
        var original = new FixedLengthListPacket { Values = new List<int> { 10, 20, 30 } };

        // WHEN
        var result = RoundTrip<FixedLengthListPacket>(original);

        // THEN: Values preserved
        result.Values.ShouldBe(new List<int> { 10, 20, 30 });

        // AND: No length prefix — wire size is exactly 3 * 4 = 12 bytes
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(12);
    }

    [Fact]
    public void FixedLength_TypedArray_Throws_WhenCountMismatch()
    {
        // GIVEN: An array with the wrong element count
        var original = new FixedLengthTypedArrayPacket { Values = new[] { 1, 2 } }; // expects 3

        // WHEN / THEN: Serialize throws
        var writer = new ArrayBufferWriter<byte>();
        Should.Throw<InvalidOperationException>(() => _serializer.Serialize(writer, original))
            .Message.ShouldContain("FixedLength");
    }

    [Fact]
    public void RoundTrip_SizedEntry_TypedArray()
    {
        // GIVEN: A packet with [SizedEntry] on an array of custom entries
        var original = new SizedEntryArrayPacket
        {
            Entries = [new SizedEntryItem { Id = 0x1234, Level = 5 }, new SizedEntryItem { Id = 0x5678, Level = 10 }]
        };

        // WHEN: Serializing and deserializing
        var result = RoundTrip<SizedEntryArrayPacket>(original);

        // THEN: Values are preserved
        result.Entries.Length.ShouldBe(2);
        result.Entries[0].Id.ShouldBe((ushort)0x1234);
        result.Entries[0].Level.ShouldBe((byte)5);
        result.Entries[1].Id.ShouldBe((ushort)0x5678);
        result.Entries[1].Level.ShouldBe((byte)10);

        // AND: Wire format is [count(1)] [entry_size(2)] [entries]
        // = 1 + 2 + 2*(2+1) = 9 bytes
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(9);
    }

    [Fact]
    public void RoundTrip_SizedEntry_List()
    {
        // GIVEN: A packet with [SizedEntry] on a List<T>
        var original = new SizedEntryListPacket
        {
            Items = [new SizedEntryItem { Id = 0x0001, Level = 99 }]
        };

        // WHEN
        var result = RoundTrip<SizedEntryListPacket>(original);

        // THEN
        result.Items.Count.ShouldBe(1);
        result.Items[0].Id.ShouldBe((ushort)0x0001);
        result.Items[0].Level.ShouldBe((byte)99);
    }

    [Fact]
    public void RoundTrip_SizedEntry_PrimitiveArray()
    {
        // GIVEN: A packet with [SizedEntry] on a primitive ushort[]
        var original = new SizedEntryPrimitiveArrayPacket { Values = [100, 200, 300] };

        // WHEN
        var result = RoundTrip<SizedEntryPrimitiveArrayPacket>(original);

        // THEN
        result.Values.ShouldBe(new ushort[] { 100, 200, 300 });

        // AND: Wire = [count(1)] [entry_size(2)] [3 * 2] = 1 + 2 + 6 = 9
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(9);
    }

    [Fact]
    public void RoundTrip_SizedEntry_WithPacketLength()
    {
        // GIVEN: [PacketLength(2)] + [SizedEntry] — 2-byte count + 2-byte entry size
        var original = new SizedEntryWithPacketLengthPacket
        {
            Entries = [new SizedEntryItem { Id = 1, Level = 2 }]
        };

        // WHEN
        var result = RoundTrip<SizedEntryWithPacketLengthPacket>(original);

        // THEN
        result.Entries.Length.ShouldBe(1);
        result.Entries[0].Id.ShouldBe((ushort)1);
        result.Entries[0].Level.ShouldBe((byte)2);

        // AND: Wire = [count(2)] [entry_size(2)] [3] = 2 + 2 + 3 = 7
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(7);
    }

    [Fact]
    public void RoundTrip_SizedEntry_CustomWidth()
    {
        // GIVEN: [SizedEntry(1)] — entry size written as 1 byte
        var original = new SizedEntryCustomWidthPacket
        {
            Entries = [new SizedEntryItem { Id = 0xABCD, Level = 42 }]
        };

        // WHEN
        var result = RoundTrip<SizedEntryCustomWidthPacket>(original);

        // THEN
        result.Entries[0].Id.ShouldBe((ushort)0xABCD);
        result.Entries[0].Level.ShouldBe((byte)42);

        // AND: Wire = [count(1)] [entry_size(1)] [3] = 1 + 1 + 3 = 5
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(5);
    }

    [Fact]
    public void RoundTrip_SizedEntry_Empty()
    {
        // GIVEN: An empty collection with [SizedEntry]
        var original = new SizedEntryArrayPacket { Entries = [] };

        // WHEN
        var result = RoundTrip<SizedEntryArrayPacket>(original);

        // THEN
        result.Entries.ShouldBeEmpty();

        // AND: Wire = [count(1)=0] [entry_size(2)] = 1 + 2 = 3
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(3);
    }

    [Fact]
    public void RoundTrip_SizedEntry_WithNeighbours()
    {
        // GIVEN: [SizedEntry] collection flanked by scalar properties
        var original = new SizedEntryWithNeighboursPacket
        {
            Before = 0xAA,
            Entries = [new SizedEntryItem { Id = 1, Level = 2 }],
            After = 0xBB
        };

        // WHEN
        var result = RoundTrip<SizedEntryWithNeighboursPacket>(original);

        // THEN: All values preserved
        result.Before.ShouldBe((byte)0xAA);
        result.Entries.Length.ShouldBe(1);
        result.Entries[0].Id.ShouldBe((ushort)1);
        result.After.ShouldBe((byte)0xBB);

        // AND: Wire = 1 + [count(1)] + [entry_size(2)] + [3] + 1 = 8
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(8);
    }

    [Fact]
    public void RoundTrip_SizedEntry_LittleEndian()
    {
        // GIVEN: [SizedEntry(2, littleEndian: true)] — entry size field is little-endian
        var original = new SizedEntryLittleEndianPacket
        {
            Entries = [new SizedEntryItem { Id = 0x1234, Level = 7 }]
        };

        // WHEN
        var result = RoundTrip<SizedEntryLittleEndianPacket>(original);

        // THEN: Values preserved
        result.Entries.Length.ShouldBe(1);
        result.Entries[0].Id.ShouldBe((ushort)0x1234);
        result.Entries[0].Level.ShouldBe((byte)7);

        // AND: Wire = [count(1)] + [entry_size_LE(2)] + entry(3) = 6
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(6);

        // AND: Verify the entry-size bytes are little-endian (0x03, 0x00) not big-endian (0x00, 0x03)
        var bytes = writer.WrittenSpan;
        bytes[1].ShouldBe((byte)0x03); // low byte first
        bytes[2].ShouldBe((byte)0x00); // high byte second
    }

    [Fact]
    public void RoundTrip_CString_FixedLength()
    {
        // GIVEN: A [CString(8)] string shorter than the field
        var original = new FixedCStringPacket { Name = "Hello" };

        // WHEN
        var result = RoundTrip<FixedCStringPacket>(original);

        // THEN: string value is preserved
        result.Name.ShouldBe("Hello");

        // AND: exactly 8 bytes written (padded with \0)
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(8);
        writer.WrittenSpan[5].ShouldBe((byte)0); // null terminator at index 5
    }

    [Fact]
    public void RoundTrip_CString_NullTerminated()
    {
        // GIVEN: A [CString] (no length) packet — wire format is string bytes + \0
        var original = new NullTerminatedCStringPacket { Name = "WAR" };

        // WHEN
        var result = RoundTrip<NullTerminatedCStringPacket>(original);

        // THEN: string value is preserved
        result.Name.ShouldBe("WAR");

        // AND: wire is exactly the encoded bytes + 1 null terminator (no fixed padding)
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(4); // 3 chars + \0
        writer.WrittenSpan[3].ShouldBe((byte)0);
    }

    [Fact]
    public void RoundTrip_CString_NullTerminated_WithNeighbours()
    {
        // GIVEN: Surrounding byte properties bracket a null-terminated CString
        var original = new NullTerminatedCStringWithNeighboursPacket
            { Before = 0xAA, Middle = "Hi", After = 0xBB };

        // WHEN
        var result = RoundTrip<NullTerminatedCStringWithNeighboursPacket>(original);

        // THEN
        result.Before.ShouldBe((byte)0xAA);
        result.Middle.ShouldBe("Hi");
        result.After.ShouldBe((byte)0xBB);

        // AND: wire = 0xAA + 'H' + 'i' + 0x00 + 0xBB = 5 bytes
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        writer.WrittenCount.ShouldBe(5);
    }

    // ── ConditionalOn ───────────────────────────────────────────────────

    [Fact]
    public void ConditionalOn_ConditionNotMet_FieldSkipped()
    {
        // Type == 1 — does NOT match 23 or 24, so Extra should be absent from wire
        var original = new ConditionalOnPacket { Type = 1, Extra = 0xDEAD, After = 0xBB };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // Type(1) + After(1) = 2 bytes. No Extra on the wire.
        bytes.Length.ShouldBe(2);
        bytes[0].ShouldBe((byte)1);    // Type
        bytes[1].ShouldBe((byte)0xBB); // After
    }

    [Fact]
    public void ConditionalOn_ConditionMet_FieldWritten()
    {
        // Type == 23 matches, so Extra should appear on the wire
        var original = new ConditionalOnPacket { Type = 23, Extra = 0x1234, After = 0xAA };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // Type(1) + Extra(2) + After(1) = 4 bytes
        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)23);
        BinaryPrimitives.ReadUInt16BigEndian(bytes[1..]).ShouldBe((ushort)0x1234);
        bytes[3].ShouldBe((byte)0xAA);
    }

    [Fact]
    public void ConditionalOn_SecondMatchValue_FieldWritten()
    {
        // Type == 24 also matches (second value in the attribute)
        var original = new ConditionalOnPacket { Type = 24, Extra = 500, After = 9 };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // Type(1) + Extra(2) + After(1) = 4 bytes
        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)24);
        BinaryPrimitives.ReadUInt16BigEndian(bytes[1..]).ShouldBe((ushort)500);
    }

    [Fact]
    public void ConditionalOn_RoundTrip_ConditionMet()
    {
        var original = new ConditionalOnPacket { Type = 23, Extra = 9999, After = 42 };
        var result = RoundTrip(original);
        result.Type.ShouldBe((byte)23);
        result.Extra.ShouldBe((ushort)9999);
        result.After.ShouldBe((byte)42);
    }

    [Fact]
    public void ConditionalOn_RoundTrip_ConditionNotMet()
    {
        var original = new ConditionalOnPacket { Type = 5, Extra = 0xFFFF, After = 77 };
        var result = RoundTrip(original);
        result.Type.ShouldBe((byte)5);
        result.Extra.ShouldBe((ushort)0); // not on wire -> stays default
        result.After.ShouldBe((byte)77);
    }

    [Fact]
    public void ConditionalOn_MultipleConditionalFields()
    {
        // Type == 24: both Bitmask (23||24) and TrophyByte (24 only) should appear
        var original = new TrophyLikePacket { Type = 24, Bitmask = 0xABCD1234, TrophyByte = 0x55, After = 0xEE };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // Type(1) + Bitmask(4) + TrophyByte(1) + After(1) = 7 bytes
        bytes.Length.ShouldBe(7);
        bytes[0].ShouldBe((byte)24);
        BinaryPrimitives.ReadUInt32BigEndian(bytes[1..]).ShouldBe(0xABCD1234u);
        bytes[5].ShouldBe((byte)0x55);
        bytes[6].ShouldBe((byte)0xEE);
    }

    [Fact]
    public void ConditionalOn_PartialMatch_OnlyMatchingFieldsWritten()
    {
        // Type == 23: Bitmask (23||24) present, but TrophyByte (24 only) absent
        var original = new TrophyLikePacket { Type = 23, Bitmask = 0x11223344, TrophyByte = 0xFF, After = 0xDD };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // Type(1) + Bitmask(4) + After(1) = 6 bytes. No TrophyByte.
        bytes.Length.ShouldBe(6);
        bytes[0].ShouldBe((byte)23);
        BinaryPrimitives.ReadUInt32BigEndian(bytes[1..]).ShouldBe(0x11223344u);
        bytes[5].ShouldBe((byte)0xDD);
    }

    [Fact]
    public void ConditionalOn_RoundTrip_TrophyLike_FullMatch()
    {
        var original = new TrophyLikePacket { Type = 24, Bitmask = 0xDEADBEEF, TrophyByte = 0x42, After = 0x01 };
        var result = RoundTrip(original);
        result.Type.ShouldBe((byte)24);
        result.Bitmask.ShouldBe(0xDEADBEEFu);
        result.TrophyByte.ShouldBe((byte)0x42);
        result.After.ShouldBe((byte)0x01);
    }

    [Fact]
    public void ConditionalOn_RoundTrip_TrophyLike_PartialMatch()
    {
        var original = new TrophyLikePacket { Type = 23, Bitmask = 0x12345678, TrophyByte = 0xFF, After = 0x02 };
        var result = RoundTrip(original);
        result.Type.ShouldBe((byte)23);
        result.Bitmask.ShouldBe(0x12345678u);
        result.TrophyByte.ShouldBe((byte)0); // not on wire -> default
        result.After.ShouldBe((byte)0x02);
    }

    [Fact]
    public void ConditionalOn_RoundTrip_TrophyLike_NoMatch()
    {
        var original = new TrophyLikePacket { Type = 1, Bitmask = 0xFFFFFFFF, TrophyByte = 0xAA, After = 0x03 };
        var result = RoundTrip(original);
        result.Type.ShouldBe((byte)1);
        result.Bitmask.ShouldBe(0u); // not on wire
        result.TrophyByte.ShouldBe((byte)0); // not on wire
        result.After.ShouldBe((byte)0x03);
    }

    [Fact]
    public void ConditionalOn_FlagsEnum_MultiBitValue_SatisfiesSingleBitCondition()
    {
        // Flags = SelfTarget|HasDamage|ShowVisual (0x07) must satisfy [ConditionalOn HasDamage (0x02)]
        var original = new FlagsConditionalPacket
        {
            Flags = CombatTestFlags.SelfTarget | CombatTestFlags.HasDamage | CombatTestFlags.ShowVisual,
            DamageAmount = 42,
            After = 0xFF
        };

        var result = RoundTrip(original);

        result.Flags.ShouldBe(original.Flags);
        result.DamageAmount.ShouldBe((ushort)42);
        result.After.ShouldBe((byte)0xFF);
    }

    [Fact]
    public void ConditionalOn_FlagsEnum_ConditionBitAbsent_FieldSkipped()
    {
        // Flags = SelfTarget only (0x01) — HasDamage (0x02) bit not set → field skipped
        var original = new FlagsConditionalPacket
        {
            Flags = CombatTestFlags.SelfTarget,
            DamageAmount = 9999,
            After = 0x77
        };

        var result = RoundTrip(original);

        result.Flags.ShouldBe(CombatTestFlags.SelfTarget);
        result.DamageAmount.ShouldBe((ushort)0); // not on wire → default
        result.After.ShouldBe((byte)0x77);
    }

    // ── PacketLength LittleEndian ───────────────────────────────────────

    [Fact]
    public void PacketLengthLE_UInt32_RoundTrip_Array()
    {
        // GIVEN: A packet with array using 4-byte LE length prefix
        var original = new PacketLengthLE_UInt32Packet
        {
            Items =
            [
                new() { Id = 1, Value = 100 },
                new() { Id = 2, Value = 200 }
            ]
        };

        // WHEN: Round-tripping
        var result = RoundTrip(original);

        // THEN: Elements are preserved
        result.Items.Length.ShouldBe(2);
        result.Items[0].Id.ShouldBe((byte)1);
        result.Items[0].Value.ShouldBe((ushort)100);
        result.Items[1].Id.ShouldBe((byte)2);
        result.Items[1].Value.ShouldBe((ushort)200);
    }

    [Fact]
    public void PacketLengthLE_UInt32_WritesLengthInLittleEndian()
    {
        // GIVEN: A packet with 2 elements and 4-byte LE length prefix
        var original = new PacketLengthLE_UInt32Packet
        {
            Items =
            [
                new() { Id = 0xAA, Value = 0x1234 },
                new() { Id = 0xBB, Value = 0x5678 }
            ]
        };

        // WHEN: Serializing
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // THEN: First 4 bytes are count=2 in LE: 02 00 00 00
        bytes[0].ShouldBe((byte)0x02);
        bytes[1].ShouldBe((byte)0x00);
        bytes[2].ShouldBe((byte)0x00);
        bytes[3].ShouldBe((byte)0x00);
        // Element 0: Id(AA) + Value(12 34) BE
        bytes[4].ShouldBe((byte)0xAA);
        bytes[5].ShouldBe((byte)0x12);
        bytes[6].ShouldBe((byte)0x34);
        // Element 1: Id(BB) + Value(56 78) BE
        bytes[7].ShouldBe((byte)0xBB);
        bytes[8].ShouldBe((byte)0x56);
        bytes[9].ShouldBe((byte)0x78);
        bytes.Length.ShouldBe(10);
    }

    [Fact]
    public void PacketLengthLE_UInt16_WritesLengthInLittleEndian()
    {
        // GIVEN: A packet with 3 ushort values and 2-byte LE length prefix
        var original = new PacketLengthLE_UInt16Packet { Values = [0x1122, 0x3344, 0x5566] };

        // WHEN: Serializing
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // THEN: First 2 bytes are count=3 in LE: 03 00
        bytes[0].ShouldBe((byte)0x03);
        bytes[1].ShouldBe((byte)0x00);
        // Values in BE (default wire encoding)
        bytes[2].ShouldBe((byte)0x11);
        bytes[3].ShouldBe((byte)0x22);
        bytes[4].ShouldBe((byte)0x33);
        bytes[5].ShouldBe((byte)0x44);
        bytes[6].ShouldBe((byte)0x55);
        bytes[7].ShouldBe((byte)0x66);
        bytes.Length.ShouldBe(8);
    }

    [Fact]
    public void PacketLengthLE_UInt16_RoundTrip()
    {
        var original = new PacketLengthLE_UInt16Packet { Values = [100, 200, 300] };
        var result = RoundTrip(original);
        result.Values.ShouldBe(new ushort[] { 100, 200, 300 });
    }

    [Fact]
    public void PacketLengthLE_List_RoundTrip()
    {
        // GIVEN: A List<T> variant with 4-byte LE length
        var original = new PacketLengthLE_ListPacket
        {
            Items =
            [
                new() { Id = 10, Value = 1000 },
                new() { Id = 20, Value = 2000 },
                new() { Id = 30, Value = 3000 }
            ]
        };

        // WHEN: Round-tripping
        var result = RoundTrip(original);

        // THEN
        result.Items.Count.ShouldBe(3);
        result.Items[0].Id.ShouldBe((byte)10);
        result.Items[0].Value.ShouldBe((ushort)1000);
        result.Items[2].Id.ShouldBe((byte)30);
        result.Items[2].Value.ShouldBe((ushort)3000);
    }

    [Fact]
    public void PacketLengthLE_Empty_WritesZeroLength()
    {
        // GIVEN: Empty collection
        var original = new PacketLengthLE_UInt32Packet { Items = [] };

        // WHEN: Serializing
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // THEN: 4 zero bytes for LE uint32 count=0
        bytes.Length.ShouldBe(4);
        bytes[0].ShouldBe((byte)0x00);
        bytes[1].ShouldBe((byte)0x00);
        bytes[2].ShouldBe((byte)0x00);
        bytes[3].ShouldBe((byte)0x00);
    }

    [Fact]
    public void PacketLengthLE_DeserializeFromKnownBytes()
    {
        // GIVEN: Known LE-prefixed bytes: count=2 (LE uint32), then 2 elements
        var data = new byte[]
        {
            0x02, 0x00, 0x00, 0x00, // count=2 LE
            0x01, 0x00, 0x0A,        // elem0: Id=1, Value=10 (BE)
            0x02, 0x00, 0x14         // elem1: Id=2, Value=20 (BE)
        };

        // WHEN: Deserializing
        var result = _serializer.Deserialize<PacketLengthLE_UInt32Packet>(data);

        // THEN
        result.Items.Length.ShouldBe(2);
        result.Items[0].Id.ShouldBe((byte)1);
        result.Items[0].Value.ShouldBe((ushort)10);
        result.Items[1].Id.ShouldBe((byte)2);
        result.Items[1].Value.ShouldBe((ushort)20);
    }

    [Fact]
    public void PacketLengthLE_WithNeighbours_RoundTrip()
    {
        // GIVEN: Packet with fields before and after the LE collection
        var original = new PacketLengthLE_WithNeighboursPacket
        {
            Before = 0xAA,
            Items =
            [
                new() { Id = 5, Value = 500 }
            ],
            After = 0xBB
        };

        // WHEN: Round-tripping
        var result = RoundTrip(original);

        // THEN
        result.Before.ShouldBe((byte)0xAA);
        result.Items.Length.ShouldBe(1);
        result.Items[0].Id.ShouldBe((byte)5);
        result.Items[0].Value.ShouldBe((ushort)500);
        result.After.ShouldBe((byte)0xBB);
    }

    [Fact]
    public void PacketLengthLE_WithNeighbours_ByteLayout()
    {
        var original = new PacketLengthLE_WithNeighboursPacket
        {
            Before = 0xFF,
            Items = [new() { Id = 1, Value = 2 }],
            After = 0xEE
        };

        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // Before(1) + count_LE(4) + elem(3) + After(1) = 9
        bytes.Length.ShouldBe(9);
        bytes[0].ShouldBe((byte)0xFF);    // Before
        bytes[1].ShouldBe((byte)0x01);    // count LE low byte
        bytes[2].ShouldBe((byte)0x00);
        bytes[3].ShouldBe((byte)0x00);
        bytes[4].ShouldBe((byte)0x00);    // count LE high byte
        bytes[5].ShouldBe((byte)0x01);    // Id
        bytes[6].ShouldBe((byte)0x00);    // Value BE high
        bytes[7].ShouldBe((byte)0x02);    // Value BE low
        bytes[8].ShouldBe((byte)0xEE);    // After
    }

    // ── NullPrefixed ────────────────────────────────────────────────────

    [Fact]
    public void NullPrefixed_Null_WritesZeroByte()
    {
        // GIVEN: nested is null
        var original = new NullPrefixedPacket { Id = 0x42, Nested = null, After = 0xFF };

        // WHEN
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // THEN: Id(1) + flag(0)(1) + After(1) = 3 bytes
        bytes.Length.ShouldBe(3);
        bytes[0].ShouldBe((byte)0x42); // Id
        bytes[1].ShouldBe((byte)0x00); // NullPrefixed flag = absent
        bytes[2].ShouldBe((byte)0xFF); // After
    }

    [Fact]
    public void NullPrefixed_NonNull_WritesFlagThenData()
    {
        // GIVEN: nested is populated
        var original = new NullPrefixedPacket
        {
            Id = 0x01,
            Nested = new NullPrefixedNestedData { X = 100, Y = 200 },
            After = 0xAA
        };

        // WHEN
        var writer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(writer, original);
        var bytes = writer.WrittenSpan;

        // THEN: Id(1) + flag(1)(1) + X(2) + Y(2) + After(1) = 7 bytes
        bytes.Length.ShouldBe(7);
        bytes[0].ShouldBe((byte)0x01); // Id
        bytes[1].ShouldBe((byte)0x01); // NullPrefixed flag = present
        BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]).ShouldBe((ushort)100); // X
        BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]).ShouldBe((ushort)200); // Y
        bytes[6].ShouldBe((byte)0xAA); // After
    }

    [Fact]
    public void NullPrefixed_RoundTrip_Null()
    {
        var original = new NullPrefixedPacket { Id = 5, Nested = null, After = 9 };
        var result = RoundTrip(original);
        result.Id.ShouldBe((byte)5);
        result.Nested.ShouldBeNull();
        result.After.ShouldBe((byte)9);
    }

    [Fact]
    public void NullPrefixed_RoundTrip_NonNull()
    {
        var original = new NullPrefixedPacket
        {
            Id = 7,
            Nested = new NullPrefixedNestedData { X = 1234, Y = 5678 },
            After = 3
        };
        var result = RoundTrip(original);
        result.Id.ShouldBe((byte)7);
        result.Nested.ShouldNotBeNull();
        result.Nested!.X.ShouldBe((ushort)1234);
        result.Nested.Y.ShouldBe((ushort)5678);
        result.After.ShouldBe((byte)3);
    }

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
    public class IEnumerablePacket { public IEnumerable<int> Values { get; set; } = Array.Empty<int>(); }
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

    public class FixedLengthByteArrayPacket
    {
        [FixedLength(4)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class FixedLengthWithNeighboursPacket
    {
        public byte Before { get; set; }
        [FixedLength(3)]
        public byte[] Fixed { get; set; } = Array.Empty<byte>();
        public byte After { get; set; }
    }

    public class PascalStringPacket
    {
        [PascalString]
        public string Name { get; set; } = "";
    }

    public class PascalStringWithNeighboursPacket
    {
        public byte Before { get; set; }
        [PascalString]
        public string Text { get; set; } = "";
        public byte After { get; set; }
    }

    public class LittleEndianInt32Packet
    {
        [LittleEndian]
        public int Value { get; set; }
    }

    public class LittleEndianUInt16Packet
    {
        [LittleEndian]
        public ushort Value { get; set; }
    }

    public class LittleEndianMixedPacket
    {
        public int BigEndian { get; set; }
        [LittleEndian]
        public int LittleEndian { get; set; }
    }

    public class FixedLengthTypedArrayPacket
    {
        [FixedLength(3)]
        public int[] Values { get; set; } = Array.Empty<int>();
    }

    public class FixedLengthTypedArrayWithNeighboursPacket
    {
        public byte Before { get; set; }
        [FixedLength(2)]
        public ushort[] Items { get; set; } = Array.Empty<ushort>();
        public byte After { get; set; }
    }

    public class FixedLengthListPacket
    {
        [FixedLength(3)]
        public List<int> Values { get; set; } = new();
    }

    public class UnsupportedTypePacket
    {
        public Dictionary<string, string> Map { get; set; } = new();
    }

    public class FixedCStringPacket
    {
        [CString(8)]
        public string Name { get; set; } = "";
    }

    public class NullTerminatedCStringPacket
    {
        [CString]
        public string Name { get; set; } = "";
    }

    public class NullTerminatedCStringWithNeighboursPacket
    {
        public byte Before { get; set; }
        [CString]
        public string Middle { get; set; } = "";
        public byte After { get; set; }
    }

    public enum TestStatus : byte
    {
        None = 0,
        Active = 1,
        Inactive = 2
    }

    public class SizedEntryItem
    {
        public ushort Id { get; set; }
        public byte Level { get; set; }
    }

    public class SizedEntryArrayPacket
    {
        [SizedEntry]
        public SizedEntryItem[] Entries { get; set; } = [];
    }

    public class SizedEntryListPacket
    {
        [SizedEntry]
        public List<SizedEntryItem> Items { get; set; } = [];
    }

    public class SizedEntryPrimitiveArrayPacket
    {
        [SizedEntry]
        public ushort[] Values { get; set; } = [];
    }

    public class SizedEntryWithPacketLengthPacket
    {
        [PacketLength(2)]
        [SizedEntry]
        public SizedEntryItem[] Entries { get; set; } = [];
    }

    public class SizedEntryCustomWidthPacket
    {
        [SizedEntry(1)]
        public SizedEntryItem[] Entries { get; set; } = [];
    }

    public class SizedEntryWithNeighboursPacket
    {
        public byte Before { get; set; }
        [SizedEntry]
        public SizedEntryItem[] Entries { get; set; } = [];
        public byte After { get; set; }
    }

    public class SizedEntryLittleEndianPacket
    {
        [SizedEntry(2, littleEndian: true)]
        public SizedEntryItem[] Entries { get; set; } = [];
    }

    public class NullPrefixedNestedData
    {
        public ushort X { get; set; }
        public ushort Y { get; set; }
    }

    public class NullPrefixedPacket
    {
        public byte Id { get; set; }
        [NullPrefixed]
        public NullPrefixedNestedData? Nested { get; set; }
        public byte After { get; set; }
    }

    public class ConditionalOnPacket
    {
        public byte Type { get; set; }
        [ConditionalOn(nameof(Type), 23, 24)]
        public ushort Extra { get; set; }
        public byte After { get; set; }
    }

    [Flags]
    public enum CombatTestFlags : byte
    {
        SelfTarget = 1 << 0, // 0x01
        HasDamage  = 1 << 1, // 0x02
        ShowVisual = 1 << 2, // 0x04
    }

    public class FlagsConditionalPacket
    {
        public CombatTestFlags Flags { get; set; }
        [ConditionalOn(nameof(Flags), CombatTestFlags.HasDamage)]
        public ushort DamageAmount { get; set; }
        public byte After { get; set; }
    }

    public class TrophyLikePacket
    {
        public byte Type { get; set; }
        [ConditionalOn(nameof(Type), 23, 24)]
        public uint Bitmask { get; set; }
        [ConditionalOn(nameof(Type), 24)]
        public byte TrophyByte { get; set; }
        public byte After { get; set; }
    }

    public class LittleEndianLengthElement
    {
        public byte Id { get; set; }
        public ushort Value { get; set; }
    }

    public class PacketLengthLE_UInt32Packet
    {
        [PacketLength(4, LittleEndian = true)]
        public LittleEndianLengthElement[] Items { get; set; } = [];
    }

    public class PacketLengthLE_UInt16Packet
    {
        [PacketLength(2, LittleEndian = true)]
        public ushort[] Values { get; set; } = [];
    }

    public class PacketLengthLE_ListPacket
    {
        [PacketLength(4, LittleEndian = true)]
        public List<LittleEndianLengthElement> Items { get; set; } = [];
    }

    public class PacketLengthLE_WithNeighboursPacket
    {
        public byte Before { get; set; }
        [PacketLength(4, LittleEndian = true)]
        public LittleEndianLengthElement[] Items { get; set; } = [];
        public byte After { get; set; }
    }
}
