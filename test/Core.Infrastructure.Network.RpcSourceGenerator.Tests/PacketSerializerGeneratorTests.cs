using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RpcSourceGenerator;
using Shouldly;

namespace Core.Infrastructure.Network.RpcSourceGenerator.Tests;

public class PacketSerializerGeneratorTests
{
    private static readonly string AttributeCode = @"
namespace Core.Infrastructure.Network
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class PacketSerializerContextAttribute : System.Attribute
    {
        public PacketSerializerContextAttribute(params System.Type[] types) { }
    }
    
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class PacketLengthAttribute : System.Attribute
    {
        public int ByteCount { get; }
        public PacketLengthAttribute(int byteCount) { ByteCount = byteCount; }
    }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class FixedLengthAttribute : System.Attribute
    {
        public int Length { get; }
        public FixedLengthAttribute(int length) { Length = length; }
    }

    public interface ICustomSerializationAttribute { }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class PascalStringAttribute : System.Attribute, ICustomSerializationAttribute { }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class LittleEndianAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class CStringAttribute : System.Attribute, ICustomSerializationAttribute
    {
        public int? Length { get; }
        public CStringAttribute() { Length = null; }
        public CStringAttribute(int length) { Length = length; }
    }

    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class SizedEntryAttribute : System.Attribute
    {
        public int ByteCount { get; }
        public bool LittleEndian { get; }
        public SizedEntryAttribute(int byteCount = 2, bool littleEndian = false) { ByteCount = byteCount; LittleEndian = littleEndian; }
    }
}";

    [Fact]
    public void GeneratesSerializer_ForSimpleType()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class SimpleMessage
    {
        public int Value { get; set; }
        public string Name { get; set; }
    }

    [PacketSerializerContext(typeof(SimpleMessage))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        
        code.ShouldContain("partial class TestContext");
        code.ShouldContain("IPacketSerializerContext");
        code.ShouldContain("TrySerialize");
        code.ShouldContain("TryDeserialize");
    }

    [Fact]
    public void GeneratesSerializer_ForNestedTypes()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Address
    {
        public string Street { get; set; }
        public string City { get; set; }
    }

    public class Person
    {
        public string Name { get; set; }
        public Address HomeAddress { get; set; }
    }

    [PacketSerializerContext(typeof(Person))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        
        // Should generate serializers for both Person and Address
        code.ShouldContain("TrySerialize");
        code.ShouldContain("TryDeserialize");
        code.ShouldContain("partial class TestContext");
    }

    [Fact]
    public void GeneratesSerializer_WithCollections()
    {
        var source = @"
using System.Collections.Generic;
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Item
    {
        public int Id { get; set; }
    }

    public class Inventory
    {
        public List<Item> Items { get; set; }
    }

    [PacketSerializerContext(typeof(Inventory))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        
        
        
        
        
    }

    [Fact]
    public void GeneratesSerializer_WithNullableProperties()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Message
    {
        public int? OptionalValue { get; set; }
        public string OptionalText { get; set; }
    }

    [PacketSerializerContext(typeof(Message))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        
        
        
    }

    [Fact]
    public void GeneratesSerializer_WithEnums()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public enum Status
    {
        Active,
        Inactive,
        Pending
    }

    public class StatusMessage
    {
        public Status CurrentStatus { get; set; }
    }

    [PacketSerializerContext(typeof(StatusMessage))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        
        
        
    }

    [Fact]
    public void GeneratesSerializer_WithPrimitiveTypes()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class AllPrimitives
    {
        public byte ByteValue { get; set; }
        public short ShortValue { get; set; }
        public int IntValue { get; set; }
        public long LongValue { get; set; }
        public bool BoolValue { get; set; }
        public float FloatValue { get; set; }
        public double DoubleValue { get; set; }
        public string StringValue { get; set; }
    }

    [PacketSerializerContext(typeof(AllPrimitives))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        
        
        
    }

    [Fact]
    public void IgnoresNonPartialClass()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Message
    {
        public int Value { get; set; }
    }

    [PacketSerializerContext(typeof(Message))]
    public class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        // Should not generate anything for non-partial classes
        result.GeneratedTrees.ShouldBeEmpty();
    }

    [Fact]
    public void GeneratesSerializer_ForMultipleRootTypes()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Message1
    {
        public int Value { get; set; }
    }

    public class Message2
    {
        public string Text { get; set; }
    }

    [PacketSerializerContext(typeof(Message1), typeof(Message2))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
    }

    [Fact]
    public void GeneratesSerializer_WithArrays()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class ArrayMessage
    {
        public int[] Numbers { get; set; }
        public string[] Names { get; set; }
    }

    [PacketSerializerContext(typeof(ArrayMessage))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        
        
        
    }

    [Fact]
    public void GeneratesSerializer_WithByteArrayProperty()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class PacketWithBytes
    {
        public ushort Header { get; set; }
        public byte[] Payload { get; set; }
        public byte Trailer { get; set; }
    }

    [PacketSerializerContext(typeof(PacketWithBytes))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Deserialize path should use ReadByteArray
        code.ShouldContain("ReadByteArray");
        // Serialize path should use WriteByteArray
        code.ShouldContain("WriteByteArray");
    }

    [Fact]
    public void GeneratesSerializer_WithByteArrayAndPacketLength()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class PacketWithSizedBytes
    {
        public byte Id { get; set; }
        [PacketLength(2)]
        public byte[] Data { get; set; }
    }

    [PacketSerializerContext(typeof(PacketWithSizedBytes))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Should use length size 2 from the attribute
        code.ShouldContain("ReadByteArray(2)");
        code.ShouldContain("WriteByteArray(obj.Data, 2)");
    }

    [Fact]
    public void GeneratesSerializer_WithPacketLengthAttribute()
    {
        var source = @"
using System.Collections.Generic;
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Item
    {
        public int Id { get; set; }
    }

    public class SmallList
    {
        [PacketLength(1)]
        public List<Item> Items { get; set; }
    }

    [PacketSerializerContext(typeof(SmallList))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);
        
        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        
        
        
    }

    [Fact]
    public void GeneratesSerializer_WithFixedLengthAttribute()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class FixedPayload
    {
        [FixedLength(8)]
        public byte[] Hash { get; set; }
    }

    [PacketSerializerContext(typeof(FixedPayload))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        code.ShouldContain("ReadFixedByteArray(8)");
        code.ShouldContain("WriteFixedByteArray");
        code.ShouldNotContain("ReadByteArray"); // no length-prefix variant should appear for this property
    }

    [Fact]
    public void GeneratesSerializer_WithPascalStringAttribute()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class ChatMessage
    {
        [PascalString]
        public string Text { get; set; }
    }

    [PacketSerializerContext(typeof(ChatMessage))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        code.ShouldContain("PascalStringAttribute().Read(ref reader)");
        code.ShouldContain("PascalStringAttribute().Write(ref writer,");
        code.ShouldNotContain("ReadString()");  // regular length-prefixed read must not appear
        code.ShouldNotContain("WriteString("); // regular length-prefixed write must not appear
    }

    [Fact]
    public void GeneratesSerializer_WithLittleEndianAttribute()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class LittleEndianPacket
    {
        [LittleEndian]
        public int Counter { get; set; }
        [LittleEndian]
        public ushort Flags { get; set; }
    }

    [PacketSerializerContext(typeof(LittleEndianPacket))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        code.ShouldContain("ReadInt32LE()");
        code.ShouldContain("WriteInt32LE(");
        code.ShouldContain("ReadUInt16LE()");
        code.ShouldContain("WriteUInt16LE(");
        code.ShouldNotContain("ReadInt32()");  // big-endian variant must not appear for this property
        code.ShouldNotContain("ReadUInt16()"); // big-endian variant must not appear for this property
    }

    [Fact]
    public void GeneratesSerializer_WithCStringAttribute_FixedLength()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class NamePacket
    {
        [CString(20)]
        public string Name { get; set; }
    }

    [PacketSerializerContext(typeof(NamePacket))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        code.ShouldContain("CStringAttribute(20).Read(ref reader)");
        code.ShouldContain("CStringAttribute(20).Write(ref writer,");
        code.ShouldNotContain("ReadCStringNullTerminated");
        code.ShouldNotContain("WriteCStringNullTerminated");
    }

    [Fact]
    public void GeneratesSerializer_WithCStringAttribute_NullTerminated()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class NamePacket
    {
        [CString]
        public string Name { get; set; }
    }

    [PacketSerializerContext(typeof(NamePacket))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        code.ShouldContain("CStringAttribute().Read(ref reader)");
        code.ShouldContain("CStringAttribute().Write(ref writer,");
        code.ShouldNotContain("ReadCStringNullTerminated");
        code.ShouldNotContain("WriteCStringNullTerminated");
    }

    [Fact]
    public void GeneratesSerializer_WithCustomSerializationAttribute()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class ShortPascalStringAttribute : System.Attribute, ICustomSerializationAttribute { }

    public class ChatMessage
    {
        [ShortPascalString]
        public string Body { get; set; }
    }

    [PacketSerializerContext(typeof(ChatMessage))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // The generator should detect the custom attribute via ICustomSerializationAttribute
        // and generate attribute instantiation + method calls (no reflection)
        code.ShouldContain("ShortPascalStringAttribute().Read(ref reader)");
        code.ShouldContain("ShortPascalStringAttribute().Write(ref writer,");
        code.ShouldNotContain("ReadString()");  // must not fall back to default string read
        code.ShouldNotContain("WriteString("); // must not fall back to default string write
    }

    [Fact]
    public void GeneratesSerializer_WithFixedLengthOnCollection()
    {
        var source = @"
using Core.Infrastructure.Network;
using System.Collections.Generic;

namespace TestNamespace
{
    public class FixedCollectionPacket
    {
        [FixedLength(4)]
        public List<int> Items { get; set; }
    }

    [PacketSerializerContext(typeof(FixedCollectionPacket))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Fixed-length collection uses a compile-time constant — no length-prefix read/write
        code.ShouldContain("const int length = 4");
        code.ShouldContain("FixedLength(4)");  // validation message
        code.ShouldNotContain("ReadByte()");   // no 1-byte length prefix read
        code.ShouldNotContain("WriteByte(");   // no 1-byte length prefix write
    }

    [Fact]
    public void GeneratesSerializer_WithSizedEntryOnArray()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Entry
    {
        public ushort Id { get; set; }
        public byte Level { get; set; }
    }

    public class Packet
    {
        [SizedEntry]
        public Entry[] Entries { get; set; }
    }

    [PacketSerializerContext(typeof(Packet))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Deserialize should read and discard entry size (2-byte default)
        code.ShouldContain("reader.ReadUInt16(); // entry size");

        // Serialize should write the computed entry size (ushort=2 + byte=1 = 3)
        code.ShouldContain("writer.WriteUInt16(3);");
    }

    [Fact]
    public void GeneratesSerializer_WithSizedEntryOnList()
    {
        var source = @"
using Core.Infrastructure.Network;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Skill
    {
        public int SkillId { get; set; }
    }

    public class SkillList
    {
        [SizedEntry]
        public List<Skill> Skills { get; set; }
    }

    [PacketSerializerContext(typeof(SkillList))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Entry size is 4 (one int), written as UInt16 (default 2-byte width)
        code.ShouldContain("writer.WriteUInt16(4);");
        code.ShouldContain("reader.ReadUInt16(); // entry size");
    }

    [Fact]
    public void GeneratesSerializer_WithSizedEntryCustomWidth()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Entry
    {
        public byte A { get; set; }
        public byte B { get; set; }
    }

    public class Packet
    {
        [SizedEntry(1)]
        public Entry[] Entries { get; set; }
    }

    [PacketSerializerContext(typeof(Packet))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Entry size is 2 (two bytes), written as a single byte (1-byte width)
        code.ShouldContain("writer.WriteByte(2);");
        code.ShouldContain("reader.ReadByte(); // entry size");
    }

    [Fact]
    public void GeneratesSerializer_WithSizedEntryAndPacketLength()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Entry
    {
        public ushort Value { get; set; }
    }

    public class Packet
    {
        [PacketLength(2)]
        [SizedEntry(2)]
        public Entry[] Entries { get; set; }
    }

    [PacketSerializerContext(typeof(Packet))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Count should use 2-byte prefix (PacketLength(2))
        code.ShouldContain("reader.ReadUInt16()");
        // Entry size should also be 2-byte UInt16 write with value 2 (one ushort)
        code.ShouldContain("writer.WriteUInt16(2);");
    }

    [Fact]
    public void GeneratesSerializer_WithSizedEntryOnPrimitiveArray()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Packet
    {
        [SizedEntry]
        public ushort[] Values { get; set; }
    }

    [PacketSerializerContext(typeof(Packet))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Entry size = 2 (ushort), default 2-byte entry size field
        code.ShouldContain("writer.WriteUInt16(2);");
        code.ShouldContain("reader.ReadUInt16(); // entry size");
    }

    [Fact]
    public void GeneratesSerializer_WithSizedEntryLittleEndian()
    {
        var source = @"
using Core.Infrastructure.Network;

namespace TestNamespace
{
    public class Item
    {
        public ushort Id { get; set; }
        public byte Level { get; set; }
    }

    public class Packet
    {
        [SizedEntry(2, littleEndian: true)]
        public Item[] Items { get; set; }
    }

    [PacketSerializerContext(typeof(Packet))]
    public partial class TestContext
    {
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();

        // Entry size = 3 (ushort Id + byte Level), written as LE UInt16
        code.ShouldContain("writer.WriteUInt16LE(3);");
        code.ShouldContain("reader.ReadUInt16LE(); // entry size");
    }

    private GeneratorTestResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree, CSharpSyntaxTree.ParseText(AttributeCode) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PacketSerializerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var result = driver.GetRunResult();
        return new GeneratorTestResult
        {
            Diagnostics = result.Diagnostics,
            GeneratedTrees = result.Results[0].GeneratedSources.Select(s => s.SyntaxTree).ToArray()
        };
    }

    private class GeneratorTestResult
    {
        public ImmutableArray<Diagnostic> Diagnostics { get; init; }
        public SyntaxTree[] GeneratedTrees { get; init; } = Array.Empty<SyntaxTree>();
    }
}

