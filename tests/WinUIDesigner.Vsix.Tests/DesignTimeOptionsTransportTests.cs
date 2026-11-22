// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;

[TestClass]
public sealed class DesignTimeOptionsTransportTests
{
    [TestMethod]
    public void TheSurfaceStoresTheOptionsOfARequestBeforeItAwaitsAnything()
    {
        // Visual Studio's ProtocolHandler runs the observers of each message on one thread, in the order the messages
        // arrive, and an async observer only up to its first await. The reply-less CloseDocument observer forgets the
        // options of the document right away, so CreateSurface has to store them before its first await: stored after
        // the wait for the construction gate, the options of a document closed while its build was queued would be
        // stored after they were forgotten, and stay until the surface process ends.
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(GetSurfaceAssemblyPath());
        TypeDefinition? service = surface.MainModule.GetType("WinUIDesigner.Surface.Services.SurfaceService");
        Assert.IsNotNull(service);
        MethodDefinition moveNext = service.NestedTypes
            .Single(type => type.Name.StartsWith("<HandleCreateSurfaceAsync>d__", StringComparison.Ordinal))
            .Methods.Single(method => method.Name == "MoveNext");
        List<string> calls = moveNext.Body.Instructions
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt)
            .Select(instruction => ((MethodReference)instruction.Operand).Name)
            .ToList();

        Assert.Contains("StoreDesignTimeOptions", calls);
        Assert.Contains("GetAwaiter", calls);
        Assert.IsLessThan(
            upperBound: calls.IndexOf("GetAwaiter"),
            value: calls.IndexOf("StoreDesignTimeOptions"),
            message: "CreateSurface stores the design-time options only after its first await."
        );
    }

    // The surface built next to these tests. The newest build wins when several configurations or platforms were built.
    private static string GetSurfaceAssemblyPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "WinUIDesigner.slnx")))
            {
                continue;
            }

            string output = Path.Combine(directory.FullName, "src", "WinUIDesigner.Surface", "bin");
            string? path = Directory.Exists(output)
                ? Directory.EnumerateFiles(output, "WinUISurface.dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            return path ?? throw new FileNotFoundException($"Could not locate WinUISurface.dll under '{output}'. Build WinUIDesigner.Surface first.");
        }

        throw new FileNotFoundException("Could not locate WinUIDesigner.slnx from the test output directory.");
    }
}
