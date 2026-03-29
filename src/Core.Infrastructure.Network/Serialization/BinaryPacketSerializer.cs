using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Core.Infrastructure.Network.Serialization.Attributes;
using FastGenericNew;

namespace Core.Infrastructure.Network.Serialization;

/// <summary>
/// Binary packet serializer that uses reflection to serialize/deserialize structs.
/// Processes properties in the order they are declared.
/// Can optionally use a source-generated context for improved performance.
/// </summary>
public class BinaryPacketSerializer : IPacketSerializer
{
    private static readonly Encoding Encoding = Encoding.GetEncoding("iso-8859-1");
    private readonly IPacketSerializerContext? _context;

    /// <summary>
    /// Creates a new BinaryPacketSerializer
    /// </summary>
    /// <param name="context">Optional source-generated context for optimized serialization</param>
    public BinaryPacketSerializer(IPacketSerializerContext? context = null)
    {
        _context = context;
    }

    /// <summary>
    /// Deserializes a packet payload into a strongly-typed struct.
    /// Properties are read in declaration order.
    /// </summary>
    public T Deserialize<T>(ReadOnlySpan<byte> payload)
    {
        // Try context first
        if (_context != null && _context.TryDeserialize(typeof(T), payload, out var result))
            return (T)result!;

        // Fall back to reflection
        var type = typeof(T);
            
        if (type.IsValueType)
            throw new InvalidOperationException($"Type {type.Name} must be a reference type");

        var instance = FastNew.CreateInstance<T>();
        var reader = new SpanReader(payload);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var ctx = new NullabilityInfoContext();

        foreach (var property in properties)
        {
            if (!property.CanWrite)
                continue;
                
            if (ctx.Create(property).WriteState is NullabilityState.Nullable && reader.IsAtEnd())
            {
                property.SetValue(instance, null);
                continue;
            }

            var value = ReadProperty(ref reader, property.PropertyType, property);
            property.SetValue(instance, value);
        }

        return instance;
    }

    /// <summary>
    /// Serializes a struct into a buffer writer.
    /// Properties are written in declaration order.
    /// </summary>
    public void Serialize<T>(IBufferWriter<byte> writer, T message)
    {
        // Try context first
        if (_context != null && _context.TrySerialize(message!, writer))
            return;

        // Fall back to reflection
        var type = typeof(T);
            
        if (type.IsValueType)
            throw new InvalidOperationException($"Type {type.Name} must be reference type");

        var spanWriter = new SpanWriter(writer);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (!property.CanRead)
                continue;

            var value = property.GetValue(message);
                
            // Skip nullable properties that are null
            var isNullable = Nullable.GetUnderlyingType(property.PropertyType) != null || !property.PropertyType.IsValueType;
            if (isNullable && value == null)
                continue;

            WriteProperty(ref spanWriter, property.PropertyType, value, property);
        }
    }

    private object ReadProperty(ref SpanReader reader, Type propertyType, PropertyInfo? propertyInfo = null)
    {
        // Get the length size from PacketLength attribute (default to 1 byte)
        var lengthSize = 1;
        var littleEndian = false;
        if (propertyInfo != null)
        {
            var packetLengthAttr = propertyInfo.GetCustomAttribute<PacketLengthAttribute>();
            if (packetLengthAttr != null)
                lengthSize = packetLengthAttr.ByteCount;
            littleEndian = propertyInfo.GetCustomAttribute<LittleEndianAttribute>() != null;
        }

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(propertyType);
        if (underlyingType != null)
        {
            propertyType = underlyingType;
        }

        if (propertyType == typeof(byte))
            return reader.ReadByte();
        if (propertyType == typeof(sbyte))
            return reader.ReadSByte();
        if (propertyType == typeof(short))
            return littleEndian ? reader.ReadInt16LE() : reader.ReadInt16();
        if (propertyType == typeof(ushort))
            return littleEndian ? reader.ReadUInt16LE() : reader.ReadUInt16();
        if (propertyType == typeof(int))
            return littleEndian ? reader.ReadInt32LE() : reader.ReadInt32();
        if (propertyType == typeof(uint))
            return littleEndian ? reader.ReadUInt32LE() : reader.ReadUInt32();
        if (propertyType == typeof(long))
            return littleEndian ? reader.ReadInt64LE() : reader.ReadInt64();
        if (propertyType == typeof(ulong))
            return littleEndian ? reader.ReadUInt64LE() : reader.ReadUInt64();
        if (propertyType == typeof(float))
            return littleEndian ? reader.ReadFloatLE() : reader.ReadFloat();
        if (propertyType == typeof(double))
            return littleEndian ? reader.ReadDoubleLE() : reader.ReadDouble();
        if (propertyType == typeof(bool))
            return reader.ReadByte() != 0;
        if (propertyType.IsEnum)
            return Enum.ToObject(propertyType,reader.ReadByte());
        if (propertyType == typeof(string))
        {
            if (propertyInfo != null)
            {
                if (propertyInfo.GetCustomAttribute<PascalStringAttribute>() != null)
                    return reader.ReadPascalString();

                var cstr = propertyInfo.GetCustomAttribute<CStringAttribute>();
                if (cstr != null)
                    return reader.ReadCString(cstr.Length);
            }

            return reader.ReadString();
        }
        if (propertyType.IsArray)
        {
            var elementType = propertyType.GetElementType()!;
            if (elementType == typeof(byte))
            {
                if (propertyInfo != null)
                {
                    var fixedLenAttr = propertyInfo.GetCustomAttribute<FixedLengthAttribute>();
                    if (fixedLenAttr != null)
                        return reader.ReadFixedByteArray(fixedLenAttr.Length);
                }
                return reader.ReadByteArray(lengthSize);
            }
            // Use generic method for arrays; [FixedLength] skips the length-prefix read
            var fixedCount = propertyInfo?.GetCustomAttribute<FixedLengthAttribute>()?.Length;
            var sizedEntryAttr = propertyInfo?.GetCustomAttribute<SizedEntryAttribute>();
            var sizedEntryWidth = sizedEntryAttr?.ByteCount;
            var sizedEntryLE = sizedEntryAttr?.LittleEndian ?? false;
            return ReadArrayGeneric(ref reader, elementType, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE);
        }

        if (propertyType.IsGenericType)
        {
            var genericTypeDef = propertyType.GetGenericTypeDefinition();
                
            // Handle List<T>, IList<T>, ICollection<T>, IEnumerable<T>
            if (genericTypeDef == typeof(List<>) ||
                genericTypeDef == typeof(IList<>) ||
                genericTypeDef == typeof(ICollection<>) ||
                genericTypeDef == typeof(IEnumerable<>))
            {
                var elementType = propertyType.GetGenericArguments()[0];
                var fixedCount = propertyInfo?.GetCustomAttribute<FixedLengthAttribute>()?.Length;
                var sizedEntryAttr = propertyInfo?.GetCustomAttribute<SizedEntryAttribute>();
                var sizedEntryWidth = sizedEntryAttr?.ByteCount;
                var sizedEntryLE = sizedEntryAttr?.LittleEndian ?? false;
                    
                // Read as array first; [FixedLength] skips the length-prefix read
                var array = ReadArrayGeneric(ref reader, elementType, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE);
                    
                // Convert to appropriate collection type
                if (genericTypeDef == typeof(List<>))
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = Activator.CreateInstance(listType, array)!;
                    return list;
                }

                // For IList<T>, ICollection<T>, IEnumerable<T>, return as array
                return array;
            }

            throw new NotSupportedException($"Generic type {propertyType.Name} is not supported");
        }

        // Custom class/struct — recurse into properties
        if (propertyType.IsClass || (propertyType.IsValueType && !propertyType.IsPrimitive && !propertyType.IsEnum))
        {
            return ReadComposite(ref reader, propertyType);
        }

        throw new NotSupportedException($"Property type {propertyType.Name} is not supported");
    }

    private object ReadComposite(ref SpanReader reader, Type type)
    {
        var instance = Activator.CreateInstance(type)!;
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            if (!property.CanWrite) continue;
            var value = ReadProperty(ref reader, property.PropertyType, property);
            property.SetValue(instance, value);
        }
        return instance;
    }

    private object ReadArrayGeneric(ref SpanReader reader, Type elementType, int lengthSize, int? fixedCount = null, int? sizedEntryWidth = null, bool sizedEntryLE = false)
    {
        // If [FixedLength] is present use the fixed count; otherwise read the length prefix
        uint length;
        if (fixedCount.HasValue)
        {
            length = (uint)fixedCount.Value;
        }
        else
        {
            length = lengthSize switch
            {
                1 => reader.ReadByte(),
                2 => reader.ReadUInt16(),
                4 => reader.ReadUInt32(),
                _ => throw new InvalidOperationException($"Invalid length size: {lengthSize}")
            };
        }

        // [SizedEntry] — read and discard the entry size field
        if (sizedEntryWidth.HasValue)
        {
            _ = sizedEntryWidth.Value switch
            {
                1 => (uint)reader.ReadByte(),
                2 => sizedEntryLE ? (uint)reader.ReadUInt16LE() : (uint)reader.ReadUInt16(),
                4 => sizedEntryLE ? reader.ReadUInt32LE() : reader.ReadUInt32(),
                _ => throw new InvalidOperationException($"Invalid sized entry width: {sizedEntryWidth.Value}")
            };
        }
            
        if (length == 0)
        {
            var emptyArray = Array.CreateInstance(elementType, 0);
            return emptyArray;
        }

        // Create array
        var array = Array.CreateInstance(elementType, (int)length);
            
        // Read each element by recursively calling ReadProperty
        for (var i = 0; i < length; i++)
        {
            var element = ReadProperty(ref reader, elementType);
            array.SetValue(element, i);
        }

        return array;
    }

    private void WriteProperty(ref SpanWriter writer, Type propertyType, object value, PropertyInfo? propertyInfo = null)
    {
        // Get the length size from PacketLength attribute (default to 1 byte)
        var lengthSize = 1;
        var littleEndian = false;
        if (propertyInfo != null)
        {
            var packetLengthAttr = propertyInfo.GetCustomAttribute<PacketLengthAttribute>();
            if (packetLengthAttr != null)
                lengthSize = packetLengthAttr.ByteCount;
            littleEndian = propertyInfo.GetCustomAttribute<LittleEndianAttribute>() != null;
        }

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(propertyType);
        if (underlyingType != null)
        {
            if (value == null)
            {
                // Write default value for null
                value = Activator.CreateInstance(underlyingType);
            }
            propertyType = underlyingType;
        }

        if (propertyType == typeof(byte))
            writer.WriteByte((byte)value);
        else if (propertyType == typeof(sbyte))
            writer.WriteSByte((sbyte)value);
        else if (propertyType == typeof(short))
            { if (littleEndian) writer.WriteInt16LE((short)value); else writer.WriteInt16((short)value); }
        else if (propertyType == typeof(ushort))
            { if (littleEndian) writer.WriteUInt16LE((ushort)value); else writer.WriteUInt16((ushort)value); }
        else if (propertyType == typeof(int))
            { if (littleEndian) writer.WriteInt32LE((int)value); else writer.WriteInt32((int)value); }
        else if (propertyType == typeof(uint))
            { if (littleEndian) writer.WriteUInt32LE((uint)value); else writer.WriteUInt32((uint)value); }
        else if (propertyType == typeof(long))
            { if (littleEndian) writer.WriteInt64LE((long)value); else writer.WriteInt64((long)value); }
        else if (propertyType == typeof(ulong))
            { if (littleEndian) writer.WriteUInt64LE((ulong)value); else writer.WriteUInt64((ulong)value); }
        else if (propertyType == typeof(float))
            { if (littleEndian) writer.WriteFloatLE((float)value); else writer.WriteFloat((float)value); }
        else if (propertyType == typeof(double))
            { if (littleEndian) writer.WriteDoubleLE((double)value); else writer.WriteDouble((double)value); }
        else if (propertyType == typeof(bool))
            writer.WriteByte((byte)((bool)value ? 1 : 0));
        else if (propertyType.IsEnum)
            writer.WriteByte(Convert.ToByte(value));
        else if (propertyType == typeof(string))
        {
            if (propertyInfo != null)
            {
                if (propertyInfo.GetCustomAttribute<PascalStringAttribute>() != null)
                {
                    writer.WritePascalString((string)value ?? string.Empty);
                    return;
                }

                var cstr = propertyInfo.GetCustomAttribute<CStringAttribute>();
                if (cstr != null)
                {
                    writer.WriteCString((string)value ?? string.Empty, cstr.Length);
                    return;
                }
            }

            writer.WriteString((string)value ?? string.Empty);
        }
        else if (propertyType.IsArray)
        {
            var elementType = propertyType.GetElementType()!;
            if (elementType == typeof(byte))
            {
                if (propertyInfo != null)
                {
                    var fixedLenAttr = propertyInfo.GetCustomAttribute<FixedLengthAttribute>();
                    if (fixedLenAttr != null)
                    {
                        writer.WriteFixedByteArray((byte[])value, fixedLenAttr.Length);
                        return;
                    }
                }
                writer.WriteByteArray((byte[])value, lengthSize);
            }
            else
            {
                // Use generic method for arrays; [FixedLength] skips writing a length prefix
                var fixedCount = propertyInfo?.GetCustomAttribute<FixedLengthAttribute>()?.Length;
                var sizedEntryAttr = propertyInfo?.GetCustomAttribute<SizedEntryAttribute>();
                var sizedEntryWidth = sizedEntryAttr?.ByteCount;
                var sizedEntryLE = sizedEntryAttr?.LittleEndian ?? false;
                WriteArrayGeneric(ref writer, value, elementType, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE);
            }
        }
        else if (propertyType.IsGenericType)
        {
            var genericTypeDef = propertyType.GetGenericTypeDefinition();
                
            // Handle List<T>, IList<T>, ICollection<T>, IEnumerable<T>
            if (genericTypeDef == typeof(List<>) ||
                genericTypeDef == typeof(IList<>) ||
                genericTypeDef == typeof(ICollection<>) ||
                genericTypeDef == typeof(IEnumerable<>))
            {
                var elementType = propertyType.GetGenericArguments()[0];
                var fixedCount = propertyInfo?.GetCustomAttribute<FixedLengthAttribute>()?.Length;
                var sizedEntryAttr = propertyInfo?.GetCustomAttribute<SizedEntryAttribute>();
                var sizedEntryWidth = sizedEntryAttr?.ByteCount;
                var sizedEntryLE = sizedEntryAttr?.LittleEndian ?? false;
                    
                // Use WriteCollection; [FixedLength] skips writing a length prefix
                WriteCollectionGeneric(ref writer, value, elementType, lengthSize, fixedCount, sizedEntryWidth, sizedEntryLE);
            }
            else
                throw new NotSupportedException($"Generic type {propertyType.Name} is not supported");
        }
        else if (propertyType.IsClass || (propertyType.IsValueType && !propertyType.IsPrimitive && !propertyType.IsEnum))
        {
            // Custom class/struct — recurse into properties
            WriteComposite(ref writer, propertyType, value!);
        }
        else
            throw new NotSupportedException($"Property type {propertyType.Name} is not supported");
    }

    private void WriteComposite(ref SpanWriter writer, Type type, object value)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var property in properties)
        {
            if (!property.CanRead) continue;
            var propValue = property.GetValue(value);
            if (propValue == null) continue;
            WriteProperty(ref writer, property.PropertyType, propValue, property);
        }
    }

    private void WriteArrayGeneric(ref SpanWriter writer, object value, Type elementType, int lengthSize, int? fixedCount = null, int? sizedEntryWidth = null, bool sizedEntryLE = false)
    {
        var array = (Array)value;
            
        if (fixedCount.HasValue)
        {
            // [FixedLength] — no length prefix; validate exact count
            if (array.Length != fixedCount.Value)
                throw new InvalidOperationException(
                    $"Array length {array.Length} does not match [FixedLength({fixedCount.Value})]");
        }
        else
        {
            // Write length prefix based on lengthSize
            switch (lengthSize)
            {
                case 1:
                    if (array.Length > byte.MaxValue)
                        throw new InvalidOperationException($"Array length {array.Length} exceeds maximum for 1-byte length ({byte.MaxValue})");
                    writer.WriteByte((byte)array.Length);
                    break;
                case 2:
                    if (array.Length > ushort.MaxValue)
                        throw new InvalidOperationException($"Array length {array.Length} exceeds maximum for 2-byte length ({ushort.MaxValue})");
                    writer.WriteUInt16((ushort)array.Length);
                    break;
                case 4:
                    writer.WriteUInt32((uint)array.Length);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid length size: {lengthSize}");
            }
        }

        // [SizedEntry] — write the entry size
        if (sizedEntryWidth.HasValue)
            WriteEntrySizeField(ref writer, elementType, sizedEntryWidth.Value, sizedEntryLE);
            
        // Write each element by recursively calling WriteProperty
        for (var i = 0; i < array.Length; i++)
        {
            var element = array.GetValue(i);
            WriteProperty(ref writer, elementType, element!);
        }
    }

    private void WriteCollectionGeneric(ref SpanWriter writer, object value, Type elementType, int lengthSize, int? fixedCount = null, int? sizedEntryWidth = null, bool sizedEntryLE = false)
    {
        // Convert to array for counting
        var enumerable = (System.Collections.IEnumerable)value;
        var list = new System.Collections.ArrayList();
        foreach (var item in enumerable)
        {
            list.Add(item);
        }
            
        if (fixedCount.HasValue)
        {
            // [FixedLength] — no length prefix; validate exact count
            if (list.Count != fixedCount.Value)
                throw new InvalidOperationException(
                    $"Collection length {list.Count} does not match [FixedLength({fixedCount.Value})]");
        }
        else
        {
            // Write length prefix based on lengthSize
            switch (lengthSize)
            {
                case 1:
                    if (list.Count > byte.MaxValue)
                        throw new InvalidOperationException($"Collection length {list.Count} exceeds maximum for 1-byte length ({byte.MaxValue})");
                    writer.WriteByte((byte)list.Count);
                    break;
                case 2:
                    if (list.Count > ushort.MaxValue)
                        throw new InvalidOperationException($"Collection length {list.Count} exceeds maximum for 2-byte length ({ushort.MaxValue})");
                    writer.WriteUInt16((ushort)list.Count);
                    break;
                case 4:
                    writer.WriteUInt32((uint)list.Count);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid length size: {lengthSize}");
            }
        }

        // [SizedEntry] — write the entry size
        if (sizedEntryWidth.HasValue)
            WriteEntrySizeField(ref writer, elementType, sizedEntryWidth.Value, sizedEntryLE);
            
        // Write each element by recursively calling WriteProperty
        foreach (var item in list)
        {
            WriteProperty(ref writer, elementType, item!);
        }
    }

    private static void WriteEntrySizeField(ref SpanWriter writer, Type elementType, int sizedEntryWidth, bool littleEndian = false)
    {
        var wireSize = ComputeWireSize(elementType)
                       ?? throw new InvalidOperationException(
                           $"Cannot compute fixed wire size for element type '{elementType.Name}' used with [SizedEntry]");
        switch (sizedEntryWidth)
        {
            case 1:
                writer.WriteByte((byte)wireSize);
                break;
            case 2:
                if (littleEndian) writer.WriteUInt16LE((ushort)wireSize); else writer.WriteUInt16((ushort)wireSize);
                break;
            case 4:
                if (littleEndian) writer.WriteUInt32LE((uint)wireSize); else writer.WriteUInt32((uint)wireSize);
                break;
        }
    }

    /// <summary>
    /// Computes the fixed wire size (in bytes) of a type at runtime.
    /// Returns null if the type contains variable-length fields.
    /// </summary>
    private static int? ComputeWireSize(Type type)
    {
        if (type == typeof(byte) || type == typeof(sbyte)) return 1;
        if (type == typeof(bool)) return 1;
        if (type == typeof(short) || type == typeof(ushort)) return 2;
        if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
        if (type.IsEnum) return 1;

        // Composite class/struct — sum all public readable property wire sizes
        if (type.IsClass || (type.IsValueType && !type.IsPrimitive))
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .ToArray();

            var total = 0;
            foreach (var prop in props)
            {
                var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

                // CString with fixed length
                var cstr = prop.GetCustomAttribute<CStringAttribute>();
                if (cstr != null && propType == typeof(string))
                {
                    if (cstr.Length == null) return null;
                    total += cstr.Length.Value;
                    continue;
                }

                // PascalString — variable
                if (prop.GetCustomAttribute<PascalStringAttribute>() != null && propType == typeof(string))
                    return null;

                // FixedLength byte[]
                if (propType == typeof(byte[]))
                {
                    var fixedLen = prop.GetCustomAttribute<FixedLengthAttribute>();
                    if (fixedLen == null) return null;
                    total += fixedLen.Length;
                    continue;
                }

                // Collection — variable
                if (propType.IsArray) return null;
                if (propType.IsGenericType)
                {
                    var genDef = propType.GetGenericTypeDefinition();
                    if (genDef == typeof(List<>) || genDef == typeof(IList<>) ||
                        genDef == typeof(ICollection<>) || genDef == typeof(IEnumerable<>))
                        return null;
                }

                // String without attribute — variable
                if (propType == typeof(string)) return null;

                var size = ComputeWireSize(propType);
                if (size == null) return null;
                total += size.Value;
            }
            return total;
        }

        return null;
    }

    /// <summary>
    /// Helper for reading from a ReadOnlySpan<byte> with position tracking.
    /// </summary>
    public ref struct SpanReader
    {
        private readonly ReadOnlySpan<byte> _span;
        private int _position;

        public SpanReader(ReadOnlySpan<byte> span)
        {
            _span = span;
            _position = 0;
        }

        public bool IsAtEnd()
        {
            return _position >= _span.Length;
        }

        public byte ReadByte()
        {
            return _span[_position++];
        }

        public sbyte ReadSByte()
        {
            return (sbyte)ReadByte();
        }

        public ushort ReadUInt16()
        {
            // Big-endian (network byte order)
            var value = BinaryPrimitives.ReadUInt16BigEndian(_span.Slice(_position, 2));
            _position += 2;
            return value;
        }

        public short ReadInt16()
        {
            // Big-endian (network byte order)
            var value = BinaryPrimitives.ReadInt16BigEndian(_span.Slice(_position, 2));
            _position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            // Big-endian (network byte order)
            var value = BinaryPrimitives.ReadUInt32BigEndian(_span.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public int ReadInt32()
        {
            var value = BinaryPrimitives.ReadInt32BigEndian(_span.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            // Big-endian (network byte order)
            var value = BinaryPrimitives.ReadUInt64BigEndian(_span.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public long ReadInt64()
        {
            var value = BinaryPrimitives.ReadInt64BigEndian(_span.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public float ReadFloat()
        {
            var value = BinaryPrimitives.ReadSingleBigEndian(_span.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public double ReadDouble()
        {
            var value = BinaryPrimitives.ReadDoubleBigEndian(_span.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public short ReadInt16LE()
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(_span.Slice(_position, 2));
            _position += 2;
            return value;
        }

        public ushort ReadUInt16LE()
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_span.Slice(_position, 2));
            _position += 2;
            return value;
        }

        public int ReadInt32LE()
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(_span.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public uint ReadUInt32LE()
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(_span.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public long ReadInt64LE()
        {
            var value = BinaryPrimitives.ReadInt64LittleEndian(_span.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public ulong ReadUInt64LE()
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(_span.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public float ReadFloatLE()
        {
            var value = BinaryPrimitives.ReadSingleLittleEndian(_span.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public double ReadDoubleLE()
        {
            var value = BinaryPrimitives.ReadDoubleLittleEndian(_span.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public string ReadString()
        {
            // String format: [Length:4][Bytes:N]
            var length = ReadUInt32();
            if (length == 0)
                return string.Empty;

            if (_position + length > _span.Length)
                throw new InvalidOperationException("String length exceeds buffer size");

            var stringBytes = _span.Slice(_position, (int)length);
            _position += (int)length;
            return Encoding.GetString(stringBytes);
        }

        public string ReadCString(int? maxLength)
        {
            if (maxLength.HasValue)
            {
                var length = maxLength.Value;
                if (length <= 0)
                    return string.Empty;

                if (_position >= _span.Length)
                    return string.Empty;

                if (_position + length > _span.Length)
                    throw new InvalidOperationException("CString length exceeds buffer size");

                var slice = _span.Slice(_position, length);
                var zeroIndex = slice.IndexOf((byte)0);
                var strLen = zeroIndex >= 0 ? zeroIndex : length;
                var result = Encoding.GetString(slice.Slice(0, strLen));

                // Advance by the full field length (fixed-size C string)
                _position += length;
                return result;
            }
            else
            {
                // Null-terminated: scan until \0, consuming the terminator
                if (_position >= _span.Length)
                    return string.Empty;

                var remaining = _span.Slice(_position);
                var zeroIndex = remaining.IndexOf((byte)0);

                if (zeroIndex < 0)
                {
                    // No null terminator — consume to end of span
                    var all = Encoding.GetString(remaining);
                    _position = _span.Length;
                    return all;
                }

                var str = Encoding.GetString(remaining.Slice(0, zeroIndex));
                _position += zeroIndex + 1; // consume string bytes + null terminator
                return str;
            }
        }

        /// <summary>Reads a Pascal string: a 1-byte length prefix followed by that many bytes.</summary>
        public string ReadPascalString()
        {
            var length = ReadByte();
            if (length == 0)
                return string.Empty;

            if (_position + length > _span.Length)
                throw new InvalidOperationException("PascalString length exceeds buffer size");

            var stringBytes = _span.Slice(_position, length);
            _position += length;
            return Encoding.GetString(stringBytes);
        }

        public byte[] ReadByteArray(int lengthSize = 4)
        {
            uint length;

            if (lengthSize == 0)
            {
                // Remainder mode: read all remaining bytes with no length prefix.
                length = (uint)(_span.Length - _position);
            }
            else
            {
                // Length-prefixed: [Length:N][Bytes:M]
                length = lengthSize switch
                {
                    1 => ReadByte(),
                    2 => ReadUInt16(),
                    4 => ReadUInt32(),
                    _ => throw new InvalidOperationException($"Invalid length size: {lengthSize}")
                };
            }

            if (length == 0)
                return Array.Empty<byte>();

            if (_position + length > _span.Length)
                throw new InvalidOperationException("Array length exceeds buffer size");

            var array = new byte[length];
            _span.Slice(_position, (int)length).CopyTo(array);
            _position += (int)length;
            return array;
        }

        /// <summary>Reads exactly <paramref name="length"/> bytes with no length prefix.</summary>
        public byte[] ReadFixedByteArray(int length)
        {
            if (length <= 0)
                return Array.Empty<byte>();

            if (_position + length > _span.Length)
                throw new InvalidOperationException($"Fixed byte array length {length} exceeds buffer size");

            var array = new byte[length];
            _span.Slice(_position, length).CopyTo(array);
            _position += length;
            return array;
        }
    }

    /// <summary>
    /// Helper for writing to an IBufferWriter<byte>.
    /// </summary>
    public readonly ref struct SpanWriter
    {
        private readonly IBufferWriter<byte> _writer;

        public SpanWriter(IBufferWriter<byte> writer)
        {
            _writer = writer;
        }

        public void WriteByte(byte value)
        {
            var span = _writer.GetSpan(1);
            span[0] = value;
            _writer.Advance(1);
        }

        public void WriteSByte(sbyte value)
        {
            WriteByte((byte)value);
        }

        public void WriteUInt16(ushort value)
        {
            // Big-endian (network byte order)
            BinaryPrimitives.WriteUInt16BigEndian(_writer.GetSpan(2), value);
            _writer.Advance(2);
        }

        public void WriteInt16(short value)
        {
            BinaryPrimitives.WriteInt16BigEndian(_writer.GetSpan(2), value);
            _writer.Advance(2);
        }

        public void WriteUInt32(uint value)
        {
            // Big-endian (network byte order)
            BinaryPrimitives.WriteUInt32BigEndian(_writer.GetSpan(4), value);
            _writer.Advance(4);
        }

        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(_writer.GetSpan(4), value);
            _writer.Advance(4);
        }

        public void WriteUInt64(ulong value)
        {
            // Big-endian (network byte order)
            BinaryPrimitives.WriteUInt64BigEndian(_writer.GetSpan(8), value);
            _writer.Advance(8);
        }

        public void WriteInt64(long value)
        {
            BinaryPrimitives.WriteInt64BigEndian(_writer.GetSpan(8), value);
            _writer.Advance(8);
        }

        public void WriteFloat(float value)
        {
            BinaryPrimitives.WriteSingleBigEndian(_writer.GetSpan(4), value);
            _writer.Advance(4);
        }

        public void WriteDouble(double value)
        {
            BinaryPrimitives.WriteDoubleBigEndian(_writer.GetSpan(8), value);
            _writer.Advance(8);
        }

        public void WriteInt16LE(short value)
        {
            BinaryPrimitives.WriteInt16LittleEndian(_writer.GetSpan(2), value);
            _writer.Advance(2);
        }

        public void WriteUInt16LE(ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(_writer.GetSpan(2), value);
            _writer.Advance(2);
        }

        public void WriteInt32LE(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_writer.GetSpan(4), value);
            _writer.Advance(4);
        }

        public void WriteUInt32LE(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_writer.GetSpan(4), value);
            _writer.Advance(4);
        }

        public void WriteInt64LE(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(_writer.GetSpan(8), value);
            _writer.Advance(8);
        }

        public void WriteUInt64LE(ulong value)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(_writer.GetSpan(8), value);
            _writer.Advance(8);
        }

        public void WriteFloatLE(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(_writer.GetSpan(4), value);
            _writer.Advance(4);
        }

        public void WriteDoubleLE(double value)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(_writer.GetSpan(8), value);
            _writer.Advance(8);
        }

        public void WriteString(string value)
        {
            // String format: [Length:4][Bytes:N]
            if (string.IsNullOrEmpty(value))
            {
                WriteUInt32(0);
                return;
            }

            var lengthSpan = _writer.GetSpan(4);
            _writer.Advance(4);
            var bytesLength = Encoding.GetBytes(value, _writer.GetSpan());
            _writer.Advance(bytesLength);
            BinaryPrimitives.WriteUInt32BigEndian(lengthSpan, (uint)bytesLength);
        }

        /// <summary>Writes a Pascal string: a 1-byte length prefix followed by the encoded bytes.
        /// Strings whose encoded length exceeds 255 bytes are silently truncated.</summary>
        public void WritePascalString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                WriteByte(0);
                return;
            }

            // Reserve 1 byte for the length prefix, then encode directly into the writer buffer.
            var lengthSpan = _writer.GetSpan(1);
            _writer.Advance(1);
            var bytesWritten = Encoding.GetBytes(value, _writer.GetSpan());
            var actualWritten = Math.Min(bytesWritten, byte.MaxValue);
            _writer.Advance(actualWritten);
            lengthSpan[0] = (byte)actualWritten;
        }

        public void WriteCString(string value, int? length)
        {
            if (length.HasValue)
            {
                var fieldLen = length.Value;
                if (fieldLen <= 0)
                    throw new InvalidOperationException("CString length must be positive");

                // Fill fixed-width field
                var span = _writer.GetSpan(fieldLen);

                if (string.IsNullOrEmpty(value))
                {
                    for (int i = 0; i < fieldLen; i++)
                        span[i] = 0;
                    _writer.Advance(fieldLen);
                    return;
                }

                var bytesWritten = Encoding.GetBytes(value, span);
                if (bytesWritten >= fieldLen)
                    throw new InvalidOperationException($"String encoded length {bytesWritten} exceeds CString field length {fieldLen}");

                // Null-terminate and zero-pad the rest
                span[bytesWritten] = 0;
                for (int i = bytesWritten + 1; i < fieldLen; i++)
                    span[i] = 0;

                _writer.Advance(fieldLen);
            }
            else
            {
                // Null-terminated: emit string bytes followed by \0
                if (string.IsNullOrEmpty(value))
                {
                    var termSpan = _writer.GetSpan(1);
                    termSpan[0] = 0;
                    _writer.Advance(1);
                    return;
                }

                var maxBytes = Encoding.GetMaxByteCount(value.Length);
                var span = _writer.GetSpan(maxBytes + 1);
                var bytesWritten = Encoding.GetBytes(value, span);
                span[bytesWritten] = 0; // null terminator
                _writer.Advance(bytesWritten + 1);
            }
        }

        public void WriteByteArray(byte[] value, int lengthSize = 4)
        {
            // Remainder mode: write raw bytes with no length prefix.
            if (lengthSize == 0)
            {
                if (value is { Length: > 0 })
                {
                    var rawSpan = _writer.GetSpan(value.Length);
                    value.CopyTo(rawSpan);
                    _writer.Advance(value.Length);
                }
                return;
            }

            // Length-prefixed: [Length:N][Bytes:M]
            if (value == null || value.Length == 0)
            {
                switch (lengthSize)
                {
                    case 1:
                        WriteByte(0);
                        break;
                    case 2:
                        WriteUInt16(0);
                        break;
                    case 4:
                        WriteUInt32(0);
                        break;
                    default:
                        throw new InvalidOperationException($"Invalid length size: {lengthSize}");
                }
                return;
            }

            // Write length based on lengthSize
            switch (lengthSize)
            {
                case 1:
                    if (value.Length > byte.MaxValue)
                        throw new InvalidOperationException($"Array length {value.Length} exceeds maximum for 1-byte length ({byte.MaxValue})");
                    WriteByte((byte)value.Length);
                    break;
                case 2:
                    if (value.Length > ushort.MaxValue)
                        throw new InvalidOperationException($"Array length {value.Length} exceeds maximum for 2-byte length ({ushort.MaxValue})");
                    WriteUInt16((ushort)value.Length);
                    break;
                case 4:
                    WriteUInt32((uint)value.Length);
                    break;
                default:
                    throw new InvalidOperationException($"Invalid length size: {lengthSize}");
            }

            var span = _writer.GetSpan(value.Length);
            value.CopyTo(span);
            _writer.Advance(value.Length);
        }

        /// <summary>Writes exactly <paramref name="length"/> bytes with no length prefix.
        /// Shorter arrays are zero-padded; longer arrays are truncated.</summary>
        public void WriteFixedByteArray(byte[] value, int length)
        {
            if (length <= 0)
                return;

            var span = _writer.GetSpan(length);
            if (value == null || value.Length == 0)
            {
                span.Slice(0, length).Fill(0);
            }
            else if (value.Length >= length)
            {
                value.AsSpan(0, length).CopyTo(span);
            }
            else
            {
                value.CopyTo(span);
                span.Slice(value.Length, length - value.Length).Fill(0);
            }
            _writer.Advance(length);
        }
    }
}