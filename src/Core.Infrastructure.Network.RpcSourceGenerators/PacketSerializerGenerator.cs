using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RpcSourceGenerator
{
    [Generator]
    public class PacketSerializerGenerator : IIncrementalGenerator
    {
        // Track collection types that need helper methods
        private class CollectionMethodTracker
        {
            public HashSet<string> DeserializeMethods { get; } = [];
            public HashSet<string> SerializeMethods { get; } = [];
            public Dictionary<string, (ITypeSymbol CollectionType, ITypeSymbol ElementType, int LengthSize, int? FixedCount)> CollectionInfo { get; } = new();
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Find all classes marked with [PacketSerializerContext]
            var contextClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax cls && cls.AttributeLists.Count > 0,
                    transform: static (ctx, _) => GetContextClassOrNull(ctx))
                .Where(static m => m is not null);

            // Combine with compilation
            var compilationAndContexts = context.CompilationProvider.Combine(contextClasses.Collect());

            // Generate source for each context
            context.RegisterSourceOutput(compilationAndContexts, static (spc, source) => Execute(source.Left, source.Right, spc));
        }

        private static ClassDeclarationSyntax? GetContextClassOrNull(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;

            // Must be partial
            if (!classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return null;

            // Check for [PacketSerializerContext] attribute
            var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
            if (symbol == null)
                return null;

            var hasContextAttribute = symbol.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "PacketSerializerContextAttribute" &&
                         (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

            return hasContextAttribute ? classDeclaration : null;
        }

        private static void Execute(Compilation compilation, ImmutableArray<ClassDeclarationSyntax?> contexts, SourceProductionContext context)
        {
            if (contexts.IsDefaultOrEmpty)
                return;

            foreach (var contextDeclaration in contexts)
            {
                if (contextDeclaration == null)
                    continue;

                var semanticModel = compilation.GetSemanticModel(contextDeclaration.SyntaxTree);
                var contextSymbol = semanticModel.GetDeclaredSymbol(contextDeclaration);
                if (contextSymbol == null)
                    continue;

                // Get types from the [PacketSerializerContext(typeof(Type1), typeof(Type2), ...)] attribute
                var contextAttribute = contextSymbol.GetAttributes()
                    .FirstOrDefault(a => a.AttributeClass?.Name == "PacketSerializerContextAttribute");

                if (contextAttribute == null || contextAttribute.ConstructorArguments.IsEmpty)
                    continue;

                var rootTypes = new List<INamedTypeSymbol>();

                // The attribute constructor takes params Type[] types
                var typesArg = contextAttribute.ConstructorArguments[0];
                if (typesArg.Kind == TypedConstantKind.Array)
                {
                    foreach (var typeConstant in typesArg.Values)
                    {
                        if (typeConstant.Value is INamedTypeSymbol typeSymbol)
                            rootTypes.Add(typeSymbol);
                    }
                }

                if (rootTypes.Count > 0)
                {
                    // Discover all types needed (root types + their reference type properties recursively)
                    var allTypes = DiscoverAllTypes(rootTypes);
                    var source = GenerateSource(contextSymbol, rootTypes, allTypes);
                    context.AddSource($"{contextSymbol.Name}_Generated.g.cs", source);
                }
            }
        }

        private static List<INamedTypeSymbol> DiscoverAllTypes(List<INamedTypeSymbol> rootTypes)
        {
            var allTypes = new List<INamedTypeSymbol>();
            var typeSet = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var toProcess = new Queue<INamedTypeSymbol>(rootTypes);

            while (toProcess.Count > 0)
            {
                var currentType = toProcess.Dequeue();
                
                // Skip if already processed
                if (!typeSet.Add(currentType))
                    continue;

                allTypes.Add(currentType);

                // Examine properties for reference types that need serialization
                var properties = currentType.GetMembers().OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public && 
                                (p.GetMethod != null || p.SetMethod != null))
                    .ToList();

                foreach (var prop in properties)
                {
                    var propType = prop.Type;
                    
                    // Get underlying type for nullables
                    if (propType is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } nullableType)
                    {
                        propType = nullableType.TypeArguments[0];
                    }

                    // Check if this property type needs its own serializer
                    if (ShouldGenerateSerializerFor(propType, out var typeToAdd))
                    {
                        toProcess.Enqueue(typeToAdd);
                    }
                    
                    // Check collection element types
                    if (IsCollectionType(propType, out var elementType) && elementType != null)
                    {
                        if (ShouldGenerateSerializerFor(elementType, out var elementTypeToAdd))
                        {
                            toProcess.Enqueue(elementTypeToAdd);
                        }
                    }
                }
            }

            return allTypes;
        }

        private static bool ShouldGenerateSerializerFor(ITypeSymbol type, out INamedTypeSymbol typeToAdd)
        {
            typeToAdd = null!;

            // Skip primitive types and special types
            if (type.SpecialType != SpecialType.None)
                return false;

            // Skip strings
            if (type.SpecialType == SpecialType.System_String)
                return false;

            // Skip enums
            if (type.TypeKind == TypeKind.Enum)
                return false;

            // Skip arrays
            if (type is IArrayTypeSymbol)
                return false;

            // Skip collections
            if (IsCollectionType(type, out _))
                return false;

            // Skip object type
            if (type.SpecialType == SpecialType.System_Object)
                return false;

            // Only consider named types (classes/structs)
            if (type is INamedTypeSymbol namedType)
            {
                // Skip generic type definitions
                if (namedType.IsUnboundGenericType)
                    return false;

                typeToAdd = namedType;
                return true;
            }

            return false;
        }

        private static string GenerateSource(INamedTypeSymbol contextSymbol, List<INamedTypeSymbol> rootTypes, List<INamedTypeSymbol> allTypes)
        {
            var namespaceName = contextSymbol.ContainingNamespace?.ToDisplayString();
            var className = contextSymbol.Name;
            var tracker = new CollectionMethodTracker();

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Buffers;");
            sb.AppendLine("using Core.Infrastructure.Network;");
            sb.AppendLine("using Core.Infrastructure.Network.Serialization;");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // Generate partial class implementing IPacketSerializerContext
            sb.AppendLine($"    public partial class {className} : IPacketSerializerContext");
            sb.AppendLine("    {");
            
            // Generate TryDeserialize method - only for root types
            sb.AppendLine("        public bool TryDeserialize(Type type, ReadOnlySpan<byte> buffer, out object? result)");
            sb.AppendLine("        {");
            sb.AppendLine("            result = null;");
            sb.AppendLine("            var reader = new BinaryPacketSerializer.SpanReader(buffer);");
            sb.AppendLine();

            foreach (var type in rootTypes)
            {
                var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.AppendLine($"            if (type == typeof({fullTypeName}))");
                sb.AppendLine("            {");
                sb.AppendLine($"                result = Deserialize{GetSafeTypeName(type)}(ref reader);");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate TrySerialize method - only for root types
            sb.AppendLine("        public bool TrySerialize(object value, IBufferWriter<byte> buffer)");
            sb.AppendLine("        {");
            sb.AppendLine("            var writer = new BinaryPacketSerializer.SpanWriter(buffer);");
            sb.AppendLine();

            foreach (var type in rootTypes)
            {
                var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var safeName = GetSafeTypeName(type);
                sb.AppendLine($"            if (value is {fullTypeName} val{safeName})");
                sb.AppendLine("            {");
                sb.AppendLine($"                Serialize{safeName}(val{safeName}, ref writer);");
                sb.AppendLine("                return true;");
                sb.AppendLine("            }");
                sb.AppendLine();
            }

            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();

            // Generate type-specific Deserialize methods for ALL types
            foreach (var type in allTypes)
            {
                GenerateDeserializeMethod(sb, type, tracker);
                sb.AppendLine();
            }

            // Generate type-specific Serialize methods for ALL types
            foreach (var type in allTypes)
            {
                GenerateSerializeMethod(sb, type, tracker);
                sb.AppendLine();
            }

            // Generate collection helper methods
            GenerateCollectionHelperMethods(sb, tracker);

            // SpanReader and SpanWriter are internal in Core.Infrastructure.Network.BinaryPacketSerializer
            // and will be used by the generated code

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static void GenerateDeserializeMethod(StringBuilder sb, INamedTypeSymbol type, CollectionMethodTracker tracker)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeName = GetSafeTypeName(type);
            sb.AppendLine($"        private {fullTypeName} Deserialize{safeName}(ref BinaryPacketSerializer.SpanReader reader)");
            sb.AppendLine("        {");

            // ALL public readable properties, in declaration order — drives wire read sequence.
            var allProps = type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            // Phase 1 — read every field in wire order.
            // Getter-only properties are consumed and discarded; settable ones go into locals.
            foreach (var prop in allProps)
            {
                var propType = prop.Type;
                var isNullable = propType.NullableAnnotation == NullableAnnotation.Annotated;
                var underlyingType = propType;
                if (isNullable && propType is INamedTypeSymbol namedNullable && namedNullable.IsGenericType)
                    underlyingType = namedNullable.TypeArguments[0];

                var readExpr = BuildReadExpression(prop, propType, underlyingType, isNullable, tracker);

                if (prop.SetMethod != null)
                    sb.AppendLine($"            var local_{prop.Name} = {readExpr};");
                else
                    sb.AppendLine($"            _ = {readExpr}; // getter-only — consumed from stream");
            }

            sb.AppendLine();

            // Phase 2 — construct object from locals (object initializer supports required properties).
            var settableProps = allProps.Where(p => p.SetMethod != null).ToList();
            sb.AppendLine($"            return new {fullTypeName}");
            sb.AppendLine("            {");
            for (var i = 0; i < settableProps.Count; i++)
            {
                var prop = settableProps[i];
                var isLast = i == settableProps.Count - 1;
                sb.AppendLine($"                {prop.Name} = local_{prop.Name}{(isLast ? "" : ",")}");
            }
            sb.AppendLine("            };");
            sb.AppendLine("        }");
        }

        private static string BuildReadExpression(IPropertySymbol prop, ITypeSymbol propType, ITypeSymbol underlyingType, bool isNullable, CollectionMethodTracker tracker)
        {
            // CString
            if (HasCStringAttribute(prop) && underlyingType.SpecialType == SpecialType.System_String)
            {
                var cstringLen = GetCStringLength(prop);
                var inner = cstringLen.HasValue ? $"reader.ReadCString({cstringLen.Value})" : "reader.ReadCString(null)";
                return isNullable ? $"reader.IsAtEnd() ? null : {inner}" : inner;
            }

            // PascalString
            if (HasPascalString(prop) && underlyingType.SpecialType == SpecialType.System_String)
                return isNullable ? "reader.IsAtEnd() ? null : reader.ReadPascalString()" : "reader.ReadPascalString()";

            // Enum
            if (underlyingType.TypeKind == TypeKind.Enum)
            {
                var enumTypeName = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                return isNullable
                    ? $"reader.IsAtEnd() ? ({enumTypeName}?)null : ({enumTypeName})reader.ReadByte()"
                    : $"({enumTypeName})reader.ReadByte()";
            }

            // Collection
            if (IsCollectionType(underlyingType, out var elementType))
            {
                var lengthSize = GetPacketLengthSize(prop);
                var fixedLen = GetFixedLength(prop);
                var methodName = RegisterCollectionDeserializeMethod(tracker, underlyingType, elementType!, lengthSize, fixedLen);
                return $"{methodName}(ref reader)";
            }

            // byte[]
            if (underlyingType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            {
                var fixedLen = GetFixedLength(prop);
                if (fixedLen.HasValue)
                    return $"reader.ReadFixedByteArray({fixedLen.Value})";
                return $"reader.ReadByteArray({GetPacketLengthSize(prop)})";
            }

            // Custom reference type
            if (ShouldGenerateSerializerFor(underlyingType, out var customType))
            {
                var customSafeName = GetSafeTypeName(customType);
                return isNullable
                    ? $"reader.IsAtEnd() ? null : Deserialize{customSafeName}(ref reader)"
                    : $"Deserialize{customSafeName}(ref reader)";
            }

            // Boolean
            if (underlyingType.SpecialType == SpecialType.System_Boolean)
                return isNullable ? "reader.IsAtEnd() ? (bool?)null : (reader.ReadByte() != 0)" : "(reader.ReadByte() != 0)";

            // Nullable primitive
            if (isNullable)
            {
                var underlyingTypeName = underlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var sb2 = new StringBuilder($"reader.IsAtEnd() ? ({underlyingTypeName}?)null : reader.");
                GenerateReadExpressionInline(sb2, propType, HasLittleEndian(prop));
                return sb2.ToString();
            }

            // Primitive
            var sbp = new StringBuilder("reader.");
            GenerateReadExpressionInline(sbp, propType, HasLittleEndian(prop));
            return sbp.ToString();
        }

        private static void GenerateReadExpressionInline(StringBuilder sb, ITypeSymbol type, bool littleEndian = false)
        {
            var underlyingType = type;
            if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                underlyingType = namedType.TypeArguments[0];
            }

            var typeName = underlyingType.SpecialType switch
            {
                SpecialType.System_Byte => "ReadByte()",
                SpecialType.System_SByte => "ReadSByte()",
                SpecialType.System_Int16 => littleEndian ? "ReadInt16LE()" : "ReadInt16()",
                SpecialType.System_UInt16 => littleEndian ? "ReadUInt16LE()" : "ReadUInt16()",
                SpecialType.System_Int32 => littleEndian ? "ReadInt32LE()" : "ReadInt32()",
                SpecialType.System_UInt32 => littleEndian ? "ReadUInt32LE()" : "ReadUInt32()",
                SpecialType.System_Int64 => littleEndian ? "ReadInt64LE()" : "ReadInt64()",
                SpecialType.System_UInt64 => littleEndian ? "ReadUInt64LE()" : "ReadUInt64()",
                SpecialType.System_Single => littleEndian ? "ReadFloatLE()" : "ReadFloat()",
                SpecialType.System_Double => littleEndian ? "ReadDoubleLE()" : "ReadDouble()",
                SpecialType.System_Boolean => "ReadByte() != 0",
                SpecialType.System_String => "ReadString()",
                _ => "null /* unsupported type */"
            };

            sb.Append(typeName);
        }

        private static void GenerateSerializeMethod(StringBuilder sb, INamedTypeSymbol type, CollectionMethodTracker tracker)
        {
            var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var safeName = GetSafeTypeName(type);
            sb.AppendLine($"        private void Serialize{safeName}({fullTypeName} obj, ref BinaryPacketSerializer.SpanWriter writer)");
            sb.AppendLine("        {");

            var properties = type.GetMembers().OfType<IPropertySymbol>()
                .Where(p => p.DeclaredAccessibility == Accessibility.Public && p.GetMethod != null)
                .ToList();

            foreach (var prop in properties)
            {
                GenerateSerializeProperty(sb, prop, $"obj.{prop.Name}", tracker);
            }

            sb.AppendLine("        }");
        }

        private static void GenerateSerializeProperty(StringBuilder sb, IPropertySymbol property, string valueExpression, CollectionMethodTracker tracker)
        {
            var propType = property.Type;
            var lengthSize = GetPacketLengthSize(property);

            var underlyingType = propType;
            var needsCast = false;

            if (propType.NullableAnnotation == NullableAnnotation.Annotated && propType is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                underlyingType = namedType.TypeArguments[0];
                needsCast = true;
            }

            var value = needsCast ? $"{valueExpression}.Value" : valueExpression;

            // Check if it's a collection first
            if (IsCollectionType(underlyingType, out var elementType))
            {
                var fixedLen = GetFixedLength(property);
                // Call discrete collection serialize method
                var methodName = RegisterCollectionSerializeMethod(tracker, underlyingType, elementType!, lengthSize, fixedLen);
                sb.AppendLine($"            {methodName}(ref writer, {value});");
                return;
            }

            // byte[] — excluded from IsCollectionType, use specialized WriteByteArray / WriteFixedByteArray
            if (underlyingType is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
            {
                var fixedLen = GetFixedLength(property);
                if (fixedLen.HasValue)
                    sb.AppendLine($"            writer.WriteFixedByteArray({value}, {fixedLen.Value});");
                else
                    sb.AppendLine($"            writer.WriteByteArray({value}, {lengthSize});");
                return;
            }

            // Check if it's an enum
            if (underlyingType.TypeKind == TypeKind.Enum)
            {
                if (needsCast)
                {
                    sb.AppendLine($"            if ({valueExpression} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                writer.WriteByte((byte){value});");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            writer.WriteByte((byte){value});");
                }
                return;
            }

            // Handle custom reference types
            if (ShouldGenerateSerializerFor(underlyingType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                if (needsCast)
                {
                    sb.AppendLine($"            if ({valueExpression} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                Serialize{customTypeSafeName}({value}, ref writer);");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            Serialize{customTypeSafeName}({value}, ref writer);");
                }
                return;
            }

            // Handle PascalString attribute on string properties
            if (HasPascalString(property) && underlyingType.SpecialType == SpecialType.System_String)
            {
                if (needsCast)
                {
                    sb.AppendLine($"            if ({valueExpression} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                writer.WritePascalString({value});");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            writer.WritePascalString({value});");
                }
                return;
            }

            // Handle CString attribute on string properties
            if (HasCStringAttribute(property) && underlyingType.SpecialType == SpecialType.System_String)
            {
                var cstrLen = GetCStringLength(property);
                var writeCall = cstrLen.HasValue
                    ? $"writer.WriteCString({value}, {cstrLen.Value})"
                    : $"writer.WriteCString({value}, null)";
                if (needsCast)
                {
                    sb.AppendLine($"            if ({valueExpression} != null)");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                {writeCall};");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"            {writeCall};");
                }
                return;
            }

            // Handle nullable primitives
            if (needsCast)
            {
                sb.AppendLine($"            if ({valueExpression} != null)");
                sb.AppendLine("            {");
                sb.Append("                writer.");
                GenerateWriteExpressionInline(sb, propType, value, HasLittleEndian(property));
                sb.AppendLine(";");
                sb.AppendLine("            }");
            }
            else
            {
                sb.Append("            writer.");
                GenerateWriteExpressionInline(sb, propType, value, HasLittleEndian(property));
                sb.AppendLine(";");
            }
        }
        private static void GenerateWriteExpressionInline(StringBuilder sb, ITypeSymbol type, string valueExpression, bool littleEndian = false)
        {
            var underlyingType = type;
            if (type.NullableAnnotation == NullableAnnotation.Annotated && type is INamedTypeSymbol namedType && namedType.IsGenericType)
            {
                underlyingType = namedType.TypeArguments[0];
            }

            var methodName = underlyingType.SpecialType switch
            {
                SpecialType.System_Byte => $"WriteByte({valueExpression})",
                SpecialType.System_SByte => $"WriteSByte({valueExpression})",
                SpecialType.System_Int16 => littleEndian ? $"WriteInt16LE({valueExpression})" : $"WriteInt16({valueExpression})",
                SpecialType.System_UInt16 => littleEndian ? $"WriteUInt16LE({valueExpression})" : $"WriteUInt16({valueExpression})",
                SpecialType.System_Int32 => littleEndian ? $"WriteInt32LE({valueExpression})" : $"WriteInt32({valueExpression})",
                SpecialType.System_UInt32 => littleEndian ? $"WriteUInt32LE({valueExpression})" : $"WriteUInt32({valueExpression})",
                SpecialType.System_Int64 => littleEndian ? $"WriteInt64LE({valueExpression})" : $"WriteInt64({valueExpression})",
                SpecialType.System_UInt64 => littleEndian ? $"WriteUInt64LE({valueExpression})" : $"WriteUInt64({valueExpression})",
                SpecialType.System_Single => littleEndian ? $"WriteFloatLE({valueExpression})" : $"WriteFloat({valueExpression})",
                SpecialType.System_Double => littleEndian ? $"WriteDoubleLE({valueExpression})" : $"WriteDouble({valueExpression})",
                SpecialType.System_Boolean => $"WriteByte((byte)({valueExpression} ? 1 : 0))",
                SpecialType.System_String => $"WriteString({valueExpression})",
                _ => "/* unsupported type */"
            };

            sb.Append(methodName);
        }

        private static bool IsCollectionType(ITypeSymbol type, out ITypeSymbol? elementType)
        {
            elementType = null;

            switch (type)
            {
                // Check for arrays (except byte[] which is handled specially)
                case IArrayTypeSymbol arrayType:
                    elementType = arrayType.ElementType;
                    return elementType.SpecialType != SpecialType.System_Byte;
                // Check for generic collections
                case INamedTypeSymbol { IsGenericType: true } namedType:
                {
                    var genericDef = namedType.ConstructedFrom;
                    var genericDefString = genericDef.ToDisplayString();

                    if (genericDefString is "System.Collections.Generic.List<T>" 
                        or "System.Collections.Generic.IList<T>" 
                        or "System.Collections.Generic.ICollection<T>"
                        or "System.Collections.Generic.IEnumerable<T>")
                    {
                        elementType = namedType.TypeArguments[0];
                        return true;
                    }

                    break;
                }
            }

            return false;
        }

        private static int GetPacketLengthSize(IPropertySymbol property)
        {
            // Look for PacketLength attribute
            var packetLengthAttr = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "PacketLengthAttribute" &&
                                    (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));
            
            if (packetLengthAttr is { ConstructorArguments.Length: > 0 })
            {
                if (packetLengthAttr.ConstructorArguments[0].Value is int byteCount)
                {
                    return byteCount;
                }
            }
            
            // Default to 1 byte
            return 1;
        }

        private static int? GetCStringLength(IPropertySymbol property)
        {
            var cstrAttr = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "CStringAttribute" &&
                                     (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

            if (cstrAttr is { ConstructorArguments.Length: > 0 })
            {
                if (cstrAttr.ConstructorArguments[0].Value is int len)
                    return len;
            }

            return null;
        }

        private static int? GetFixedLength(IPropertySymbol property)
        {
            var fixedLenAttr = property.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "FixedLengthAttribute" &&
                                     (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

            if (fixedLenAttr is { ConstructorArguments.Length: > 0 })
            {
                if (fixedLenAttr.ConstructorArguments[0].Value is int len)
                    return len;
            }

            return null;
        }

        private static bool HasPascalString(IPropertySymbol property) =>
            property.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "PascalStringAttribute" &&
                          (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

        private static bool HasCStringAttribute(IPropertySymbol property) =>
            property.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "CStringAttribute" &&
                          (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

        private static bool HasLittleEndian(IPropertySymbol property) =>
            property.GetAttributes()
                .Any(a => a.AttributeClass?.Name == "LittleEndianAttribute" &&
                          (a.AttributeClass?.ContainingNamespace?.ToDisplayString().StartsWith("Core.Infrastructure.Network") ?? false));

        // Register a collection deserialize method and return its name
        private static string RegisterCollectionDeserializeMethod(CollectionMethodTracker tracker, ITypeSymbol collectionType, ITypeSymbol elementType, int lengthSize, int? fixedCount = null)
        {
            var methodName = fixedCount.HasValue
                ? $"DeserializeCollection_{GetSafeTypeName(collectionType)}_fixed_{fixedCount.Value}"
                : $"DeserializeCollection_{GetSafeTypeName(collectionType)}_{lengthSize}";
            
            if (tracker.DeserializeMethods.Add(methodName))
            {
                tracker.CollectionInfo[methodName] = (collectionType, elementType, lengthSize, fixedCount);
            }
            
            return methodName;
        }

        // Register a collection serialize method and return its name
        private static string RegisterCollectionSerializeMethod(CollectionMethodTracker tracker, ITypeSymbol collectionType, ITypeSymbol elementType, int lengthSize, int? fixedCount = null)
        {
            var methodName = fixedCount.HasValue
                ? $"SerializeCollection_{GetSafeTypeName(collectionType)}_fixed_{fixedCount.Value}"
                : $"SerializeCollection_{GetSafeTypeName(collectionType)}_{lengthSize}";
            
            if (tracker.SerializeMethods.Add(methodName))
            {
                tracker.CollectionInfo[methodName] = (collectionType, elementType, lengthSize, fixedCount);
            }
            
            return methodName;
        }

        // Generate all collection helper methods
        private static void GenerateCollectionHelperMethods(StringBuilder sb, CollectionMethodTracker tracker)
        {
            // Generate deserialize methods
            foreach (var methodName in tracker.DeserializeMethods)
            {
                var (collectionType, elementType, lengthSize, fixedCount) = tracker.CollectionInfo[methodName];
                GenerateCollectionDeserializeMethod(sb, methodName, collectionType, elementType, lengthSize, fixedCount);
                sb.AppendLine();
            }

            // Generate serialize methods
            foreach (var methodName in tracker.SerializeMethods)
            {
                var (collectionType, elementType, lengthSize, fixedCount) = tracker.CollectionInfo[methodName];
                GenerateCollectionSerializeMethod(sb, methodName, collectionType, elementType, lengthSize, fixedCount);
                sb.AppendLine();
            }
        }

        private static void GenerateCollectionDeserializeMethod(StringBuilder sb, string methodName, ITypeSymbol collectionType, ITypeSymbol elementType, int lengthSize, int? fixedCount = null)
        {
            var collectionTypeName = collectionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            sb.AppendLine($"        private {collectionTypeName} {methodName}(ref BinaryPacketSerializer.SpanReader reader)");
            sb.AppendLine("        {");
            
            if (fixedCount.HasValue)
            {
                // [FixedLength] — use the compile-time count directly, no length prefix read
                sb.AppendLine($"            const int length = {fixedCount.Value};");
                sb.AppendLine($"            if (length == 0) return {GetEmptyCollectionExpression(collectionType, elementType)};");
            }
            else
            {
                // Read length from the span
                sb.AppendLine($"            var length = {GenerateLengthRead(lengthSize)};");
                sb.AppendLine($"            if (length == 0) return {GetEmptyCollectionExpression(collectionType, elementType)};");
            }
            sb.AppendLine();
            
            // Create array to hold elements
            sb.AppendLine($"            var array = new {elementTypeName}[length];");
            sb.AppendLine("            for (int i = 0; i < length; i++)");
            sb.AppendLine("            {");
            
            // Generate element reading code
            sb.Append("                array[i] = ");
            GenerateElementRead(sb, elementType);
            sb.AppendLine(";");
            
            sb.AppendLine("            }");
            sb.AppendLine();
            
            // Convert to appropriate collection type if needed
            if (collectionType is IArrayTypeSymbol)
            {
                sb.AppendLine("            return array;");
            }
            else if (collectionType is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                var genericDef = namedType.ConstructedFrom.ToDisplayString();
                sb.AppendLine(genericDef == "System.Collections.Generic.List<T>"
                    ? $"            return new System.Collections.Generic.List<{elementTypeName}>(array);"
                    // For IList<T>, ICollection<T>, IEnumerable<T>, return as array
                    : "            return array;");
            }
            
            sb.AppendLine("        }");
        }

        private static void GenerateCollectionSerializeMethod(StringBuilder sb, string methodName, ITypeSymbol collectionType, ITypeSymbol elementType, int lengthSize, int? fixedCount = null)
        {
            var collectionTypeName = collectionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            
            sb.AppendLine($"        private void {methodName}(ref BinaryPacketSerializer.SpanWriter writer, {collectionTypeName} collection)");
            sb.AppendLine("        {");
            
            // Get count - handle different collection types
            string countExpression = collectionType is IArrayTypeSymbol ? "collection.Length" : "collection.Count";
            
            if (fixedCount.HasValue)
            {
                // [FixedLength] — no length prefix; validate exact count at runtime
                sb.AppendLine($"            var count = {countExpression};");
                sb.AppendLine($"            if (count != {fixedCount.Value})");
                sb.AppendLine($"                throw new System.InvalidOperationException($\"Collection length {{count}} does not match [FixedLength({fixedCount.Value})]\");");
            }
            else
            {
                // Write length prefix
                sb.AppendLine($"            var count = {countExpression};");
                GenerateLengthWrite(sb, "count", lengthSize);
            }
            sb.AppendLine();
            
            // Loop through and write each element
            sb.AppendLine("            foreach (var item in collection)");
            sb.AppendLine("            {");
            
            sb.Append("                ");
            GenerateElementWrite(sb, elementType, "item");
            sb.AppendLine(";");
            
            sb.AppendLine("            }");
            
            sb.AppendLine("        }");
        }

        private static string GenerateLengthRead(int lengthSize)
        {
            return lengthSize switch
            {
                1 => "reader.ReadByte()",
                2 => "reader.ReadUInt16()",
                4 => "reader.ReadUInt32()",
                _ => throw new InvalidOperationException($"Invalid length size: {lengthSize}")
            };
        }

        private static void GenerateLengthWrite(StringBuilder sb, string countVar, int lengthSize)
        {
            switch (lengthSize)
            {
                case 1:
                    sb.AppendLine($"            if ({countVar} > byte.MaxValue)");
                    sb.AppendLine($"                throw new System.InvalidOperationException($\"Collection length {{{countVar}}} exceeds maximum for 1-byte length ({{byte.MaxValue}})\");");
                    sb.AppendLine($"            writer.WriteByte((byte){countVar});");
                    break;
                case 2:
                    sb.AppendLine($"            if ({countVar} > ushort.MaxValue)");
                    sb.AppendLine($"                throw new System.InvalidOperationException($\"Collection length {{{countVar}}} exceeds maximum for 2-byte length ({{ushort.MaxValue}})\");");
                    sb.AppendLine($"            writer.WriteUInt16((ushort){countVar});");
                    break;
                case 4:
                    sb.AppendLine($"            writer.WriteUInt32((uint){countVar});");
                    break;
                default:
                    throw new InvalidOperationException($"Invalid length size: {lengthSize}");
            }
        }

        private static string GetEmptyCollectionExpression(ITypeSymbol collectionType, ITypeSymbol elementType)
        {
            var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            if (collectionType is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                var genericDef = namedType.ConstructedFrom.ToDisplayString();
                if (genericDef == "System.Collections.Generic.List<T>")
                {
                    return $"new System.Collections.Generic.List<{elementTypeName}>()";
                }
            }

            return $"System.Array.Empty<{elementTypeName}>()";
        }

        private static void GenerateElementRead(StringBuilder sb, ITypeSymbol elementType)
        {
            // Handle custom reference types
            if (ShouldGenerateSerializerFor(elementType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                sb.Append($"Deserialize{customTypeSafeName}(ref reader)");
                return;
            }
            
            // Handle nested collections
            if (IsCollectionType(elementType, out _))
            {
                // For nested collections, recursively call the collection deserialize method
                // This will be handled by the tracker system
                throw new NotSupportedException("Nested collections are not yet supported in discrete methods");
            }
            
            // Handle enums
            if (elementType.TypeKind == TypeKind.Enum)
            {
                var enumTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                sb.Append($"({enumTypeName})reader.ReadByte()");
                return;
            }
            
            // Handle primitives
            var readMethod = elementType.SpecialType switch
            {
                SpecialType.System_Byte => "reader.ReadByte()",
                SpecialType.System_SByte => "reader.ReadSByte()",
                SpecialType.System_Int16 => "reader.ReadInt16()",
                SpecialType.System_UInt16 => "reader.ReadUInt16()",
                SpecialType.System_Int32 => "reader.ReadInt32()",
                SpecialType.System_UInt32 => "reader.ReadUInt32()",
                SpecialType.System_Int64 => "reader.ReadInt64()",
                SpecialType.System_UInt64 => "reader.ReadUInt64()",
                SpecialType.System_Single => "reader.ReadFloat()",
                SpecialType.System_Double => "reader.ReadDouble()",
                SpecialType.System_Boolean => "(reader.ReadByte() != 0)",
                SpecialType.System_String => "reader.ReadString()",
                _ => throw new NotSupportedException($"Element type {elementType.Name} is not supported")
            };
            
            sb.Append(readMethod);
        }

        private static void GenerateElementWrite(StringBuilder sb, ITypeSymbol elementType, string itemVar)
        {
            // Handle custom reference types
            if (ShouldGenerateSerializerFor(elementType, out var customType))
            {
                var customTypeSafeName = GetSafeTypeName(customType);
                sb.Append($"Serialize{customTypeSafeName}({itemVar}, ref writer)");
                return;
            }
            
            // Handle nested collections
            if (IsCollectionType(elementType, out _))
            {
                // For nested collections, would need to call collection serialize method
                throw new NotSupportedException("Nested collections are not yet supported in discrete methods");
            }
            
            // Handle enums
            if (elementType.TypeKind == TypeKind.Enum)
            {
                sb.Append($"writer.WriteByte((byte){itemVar})");
                return;
            }
            
            // Handle primitives
            var writeMethod = elementType.SpecialType switch
            {
                SpecialType.System_Byte => $"writer.WriteByte({itemVar})",
                SpecialType.System_SByte => $"writer.WriteSByte({itemVar})",
                SpecialType.System_Int16 => $"writer.WriteInt16({itemVar})",
                SpecialType.System_UInt16 => $"writer.WriteUInt16({itemVar})",
                SpecialType.System_Int32 => $"writer.WriteInt32({itemVar})",
                SpecialType.System_UInt32 => $"writer.WriteUInt32({itemVar})",
                SpecialType.System_Int64 => $"writer.WriteInt64({itemVar})",
                SpecialType.System_UInt64 => $"writer.WriteUInt64({itemVar})",
                SpecialType.System_Single => $"writer.WriteFloat({itemVar})",
                SpecialType.System_Double => $"writer.WriteDouble({itemVar})",
                SpecialType.System_Boolean => $"writer.WriteByte((byte)({itemVar} ? 1 : 0))",
                SpecialType.System_String => $"writer.WriteString({itemVar})",
                _ => throw new NotSupportedException($"Element type {elementType.Name} is not supported")
            };
            
            sb.Append(writeMethod);
        }

        private static string GetSafeTypeName(ITypeSymbol type)
        {
            // Create a safe method name from the type
            var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            name = name.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace("[", "_").Replace("]", "_").Replace(",", "_").Replace(" ", "");
            return name;
        }

    }
}
