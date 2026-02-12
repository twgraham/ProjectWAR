using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using FrameWork.NetWork.SourceGenerators;
using Shouldly;

namespace Tests.RpcSourceGenerator;

public class RpcSourceGeneratorTests
{
    private static readonly string HandlerBaseCode = @"
namespace FrameWork.NetWork.V4
{
    public abstract class PacketHandler { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class RpcAttribute : System.Attribute
    {
        public byte Opcode { get; }
        public byte? ResponseOpcode { get; }
        public RpcAttribute(byte opcode) { Opcode = opcode; }
        public RpcAttribute(byte opcode, byte responseOpcode) { Opcode = opcode; ResponseOpcode = responseOpcode; }
    }

    [System.AttributeUsage(System.AttributeTargets.Parameter)]
    public class FromServicesAttribute : System.Attribute { }

    public interface IPacketSerializer
    {
        T Deserialize<T>(System.ReadOnlySpan<byte> data);
        void Serialize<T>(System.Buffers.IBufferWriter<byte> writer, T value);
    }

    public interface IConnectionContext
    {
        string RemoteAddress { get; }
        void SendResponse<T>(byte opcode, T response);
        void Disconnect(object reason);
        System.Collections.Generic.IDictionary<string, object> Items { get; }
        void OnDispatchError(byte opcode, System.Exception exception);
    }

    public interface IPacketDispatcher<in THandler> where THandler : PacketHandler
    {
        void Dispatch(THandler handler, byte opcode, System.ReadOnlyMemory<byte> payload,
            System.IServiceProvider services, IPacketSerializer serializer, IConnectionContext connection);
    }
}";

    [Fact]
    public void GeneratesDispatcher_WithSynchronousMethod_NoParameters()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void HandlePing()
        {
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("case 0x01:");
        code.ShouldContain("handler.HandlePing()");
        code.ShouldContain("class Dispatcher : IPacketDispatcher<TestHandler>");
    }

    [Fact]
    public void GeneratesDispatcher_WithSynchronousMethod_WithRequest()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class LoginRequest { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x10)]
        public void HandleLogin(LoginRequest request)
        {
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("case 0x10:");
        code.ShouldContain("serializer.Deserialize<TestNamespace.LoginRequest>(payload.Span)");
        code.ShouldContain("handler.HandleLogin(request)");
    }

    [Fact]
    public void GeneratesDispatcher_WithSynchronousMethod_WithResponse()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class LoginRequest { }
    public class LoginResponse { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x10, 0x11)]
        public LoginResponse HandleLogin(LoginRequest request)
        {
            return new LoginResponse();
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("case 0x10:");
        code.ShouldContain("var response = handler.HandleLogin(request);");
        code.ShouldContain("connection.SendResponse(0x11, response)");
    }

    [Fact]
    public void GeneratesDispatcher_WithAsyncMethod_ReturnsTask()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class PingRequest { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public async Task HandlePing(PingRequest request)
        {
            await Task.Delay(10);
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("case 0x01:");
        code.ShouldContain("_ = DispatchAsync_HandlePing(handler, request, connection);");
        code.ShouldContain("private static async Task DispatchAsync_HandlePing(");
    }

    [Fact]
    public void GeneratesDispatcher_WithAsyncMethod_ReturnsTaskWithResponse()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class LoginRequest { }
    public class LoginResponse { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x10, 0x11)]
        public async Task<LoginResponse> HandleLogin(LoginRequest request)
        {
            return await Task.FromResult(new LoginResponse());
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("case 0x10:");
        code.ShouldContain("_ = DispatchAsync_HandleLogin(handler, request, connection);");
        code.ShouldContain("var response = await handler.HandleLogin(request);");
        code.ShouldContain("connection.SendResponse(0x11, response)");
    }

    [Fact]
    public void GeneratesDispatcher_WithFromServicesParameter()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class MyService { }
    public class LoginResponse { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x10, 0x11)]
        public async Task<LoginResponse> HandleLogin([FromServices] MyService svc)
        {
            return await Task.FromResult(new LoginResponse());
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("var __scope = services.CreateScope();");
        code.ShouldContain("GetRequiredService<TestNamespace.MyService>()");
        code.ShouldContain("__scope.Dispose()");
    }

    [Fact]
    public void GeneratesDispatcher_WithConnectionContextParameter()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void HandlePing(IConnectionContext context)
        {
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("handler.HandlePing(connection)");
    }

    [Fact]
    public void GeneratesDispatcher_WithMixedParameters()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class LoginRequest { }
    public class LoginResponse { }
    public class MyService { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x10, 0x11)]
        public LoginResponse HandleLogin(LoginRequest request, IConnectionContext context, [FromServices] MyService svc)
        {
            return new LoginResponse();
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("serializer.Deserialize<TestNamespace.LoginRequest>(payload.Span)");
        code.ShouldContain("using var __scope = services.CreateScope();");
        code.ShouldContain("GetRequiredService<TestNamespace.MyService>()");
        code.ShouldContain("handler.HandleLogin(request, connection, __svc_svc)");
    }

    [Fact]
    public void ReportsDiagnostic_ForDuplicateOpcodes()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void HandlePing()
        {
        }

        [Rpc(0x01)]
        public void HandlePing2()
        {
        }
    }
}";

        var result = RunGenerator(source);

        var error = result.Diagnostics.FirstOrDefault(d => d.Id == "RPC001");
        error.ShouldNotBeNull();
        error.Severity.ShouldBe(DiagnosticSeverity.Error);
        Assert.Contains("0x01", error.GetMessage());
    }

    [Fact]
    public void ReportsDiagnostic_ForMultipleRequestParameters()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class Param1 { }
    public class Param2 { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void HandleInvalid(Param1 p1, Param2 p2)
        {
        }
    }
}";

        var result = RunGenerator(source);

        var error = result.Diagnostics.FirstOrDefault(d => d.Id == "RPC002");
        error.ShouldNotBeNull();
        error.Severity.ShouldBe(DiagnosticSeverity.Error);
    }

    [Fact]
    public void IgnoresNonPartialClass()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void HandlePing()
        {
        }
    }
}";

        var result = RunGenerator(source);
        result.GeneratedTrees.ShouldBeEmpty();
    }

    [Fact]
    public void IgnoresClassNotInheritingFromPacketHandler()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public partial class TestHandler
    {
        [Rpc(0x01)]
        public void HandlePing()
        {
        }
    }
}";

        var result = RunGenerator(source);
        result.GeneratedTrees.ShouldBeEmpty();
    }

    [Fact]
    public void GeneratesNothing_ForClassWithoutRpcMethods()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public partial class TestHandler : PacketHandler
    {
        public void RegularMethod()
        {
        }
    }
}";

        var result = RunGenerator(source);
        result.GeneratedTrees.ShouldBeEmpty();
    }

    [Fact]
    public void GeneratesDispatcher_WithMultipleMethods()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class Request1 { }
    public class Request2 { }
    public class Response2 { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void HandleMethod1(Request1 request)
        {
        }

        [Rpc(0x02)]
        public async Task<Response2> HandleMethod2(Request2 request)
        {
            return await Task.FromResult(new Response2());
        }

        [Rpc(0x03)]
        public void HandleMethod3()
        {
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        result.GeneratedTrees.ShouldHaveSingleItem();
        var code = result.GeneratedTrees[0].ToString();

        code.ShouldContain("case 0x01:");
        code.ShouldContain("case 0x02:");
        code.ShouldContain("case 0x03:");
        code.ShouldContain("handler.HandleMethod1(request)");
        code.ShouldContain("_ = DispatchAsync_HandleMethod2(handler, request, connection)");
        code.ShouldContain("handler.HandleMethod3()");
    }

    [Fact]
    public void GeneratesDispatcher_WithDefaultResponseOpcode()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class LoginRequest { }
    public class LoginResponse { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x10)]
        public LoginResponse HandleLogin(LoginRequest request)
        {
            return new LoginResponse();
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("connection.SendResponse(0x10, response)");
    }

    [Fact]
    public void AllowsMultipleFromServicesParameters()
    {
        var source = @"
using System;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class Svc1 { }
    public class Svc2 { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public void Handle([FromServices] Svc1 svc1, [FromServices] Svc2 svc2)
        {
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        code.ShouldContain("GetRequiredService<TestNamespace.Svc1>()");
        code.ShouldContain("GetRequiredService<TestNamespace.Svc2>()");
        code.ShouldContain("handler.Handle(__svc_svc1, __svc_svc2)");
    }

    [Fact]
    public void AsyncWithServices_CreatesNonDisposableScope_AndDisposesInFinally()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using FrameWork.NetWork.V4;

namespace TestNamespace
{
    public class MyService { }

    public partial class TestHandler : PacketHandler
    {
        [Rpc(0x01)]
        public async Task HandleAsync([FromServices] MyService svc)
        {
            await Task.Delay(10);
        }
    }
}";

        var result = RunGenerator(source);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ShouldBeEmpty();
        var code = result.GeneratedTrees[0].ToString();
        // Async handler should NOT use 'using' (scope passed to wrapper)
        code.ShouldContain("var __scope = services.CreateScope();");
        Assert.DoesNotContain("using var __scope", code);
        // Wrapper should dispose scope in finally
        code.ShouldContain("__scope.Dispose()");
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
            new[] { syntaxTree, CSharpSyntaxTree.ParseText(HandlerBaseCode) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new FrameWork.NetWork.SourceGenerators.RpcSourceGenerator();
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
