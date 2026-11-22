// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
#if NET9_0_OR_GREATER
using System.Reflection.Emit;
#endif

/// <summary>
/// The runtime rules that the surface relies on for the assemblies it carries itself: when a project references a
/// newer version of one of them, and when a project ships a copy of one. System.Text.Json stands in for a projection:
/// every test host carries it, as the surface carries its projections.
/// </summary>
[TestClass]
public sealed class OlderCopyBindingTests
{
    // The runtime remembers a bind for the version that was asked for, also one that a handler served: a later
    // reference to the same version is bound without the handler. Each test therefore asks for a version of its own,
    // which no runtime carries, and none of them depends on the order in which the tests run.
    private const ushort _UnservedReferenceMajorVersion = 98;

    private const ushort _ServedReferenceMajorVersion = 99;

    private const ushort _NewerStagedCopyMajorVersion = 97;

    private const ushort _OlderStagedCopyMajorVersion = 1;

#if NET9_0_OR_GREATER
    [TestMethod]
    public void TheRuntimeDoesNotBindANewerReferenceToTheCarriedCopy()
    {
        Func<string> probe = CreateProbe("OlderCopyBindingProbe.Unserved", _UnservedReferenceMajorVersion);

        Assert.Throws<IOException>(() => probe());
    }

    [TestMethod]
    public void TheCarriedCopyThatAHandlerReturnsServesANewerReference()
    {
        Func<string> probe = CreateProbe("OlderCopyBindingProbe.Served", _ServedReferenceMajorVersion);
        int calls = 0;
        Func<AssemblyLoadContext, AssemblyName, Assembly?> handler = (context, name) =>
        {
            if (name.Name != "System.Text.Json")
            {
                return null;
            }

            calls++;
            return context.LoadFromAssemblyName(new AssemblyName(name.Name));
        };

        AssemblyLoadContext.Default.Resolving += handler;
        try
        {
            Assert.AreEqual(typeof(JsonSerializerOptions).AssemblyQualifiedName, probe());
            Assert.AreNotEqual(0, calls);
        }
        finally
        {
            AssemblyLoadContext.Default.Resolving -= handler;
        }
    }
#endif

    // The two rules below are why the surface never loads the project's copy of an assembly it carries from its
    // staged path: a newer copy is refused, and an older one is the carried assembly anyway.
    [TestMethod]
    public void TheRuntimeDoesNotLoadANewerCopyOfACarriedAssemblyBesideIt()
    {
        using var staged = new StagedCopy(_NewerStagedCopyMajorVersion);

        // The HRESULT of the FileLoadException depends on the runtime: 0x80131040 on .NET 10, "the located assembly's
        // manifest definition does not match the assembly reference", and 0x80131621 on .NET 8.
        Assert.Throws<FileLoadException>(() => AssemblyLoadContext.Default.LoadFromAssemblyPath(staged.FilePath));
    }

    [TestMethod]
    public void TheRuntimeReturnsTheCarriedAssemblyForAnOlderCopyOfIt()
    {
        using var staged = new StagedCopy(_OlderStagedCopyMajorVersion);

        Assembly loaded = AssemblyLoadContext.Default.LoadFromAssemblyPath(staged.FilePath);

        Assert.AreSame(typeof(JsonSerializerOptions).Assembly, loaded);
    }

#if NET9_0_OR_GREATER
    // An assembly whose method returns the assembly-qualified name of JsonSerializerOptions, with its reference to
    // System.Text.Json raised to a version that no runtime carries. PersistedAssemblyBuilder writes it, and exists from
    // .NET 9 on: this is why the two tests that use it are left out of the .NET 8 flavour.
    private static Func<string> CreateProbe(string name, ushort referencedMajorVersion)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        TypeBuilder type = builder.DefineDynamicModule(name).DefineType("Probe", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        MethodBuilder method = type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(string), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldtoken, typeof(JsonSerializerOptions));
        il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!);
        il.Emit(OpCodes.Callvirt, typeof(Type).GetProperty(nameof(Type.AssemblyQualifiedName))!.GetMethod!);
        il.Emit(OpCodes.Ret);
        type.CreateType();

        using var stream = new MemoryStream();
        builder.Save(stream);
        byte[] image = stream.ToArray();
        RaiseReferencedVersion(image, "System.Text.Json", referencedMajorVersion);

        Assembly probe = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
        return probe.GetType("Probe")!.GetMethod("Run")!.CreateDelegate<Func<string>>();
    }

    // Writes the major version, and zeros for the other parts, into the version of a row of the AssemblyRef table.
    private static void RaiseReferencedVersion(byte[] image, string assembly, ushort majorVersion)
    {
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader metadata = pe.GetMetadataReader();
        int table = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.AssemblyRef);
        int rowSize = metadata.GetTableRowSize(TableIndex.AssemblyRef);
        foreach (AssemblyReferenceHandle handle in metadata.AssemblyReferences)
        {
            if (metadata.GetString(metadata.GetAssemblyReference(handle).Name) == assembly)
            {
                // MajorVersion, MinorVersion, BuildNumber and RevisionNumber are the first eight bytes of the row.
                Span<byte> version = image.AsSpan(table + ((MetadataTokens.GetRowNumber(handle) - 1) * rowSize), 8);
                BinaryPrimitives.WriteUInt16LittleEndian(version, majorVersion);
                version[2..].Clear();
                return;
            }
        }

        Assert.Fail($"The probe does not reference {assembly}.");
    }
#endif

    // Writes the major version, and zeros for the other parts, into the version of the row of the Assembly table,
    // which describes the assembly itself.
    private static void SetAssemblyVersion(byte[] image, ushort majorVersion)
    {
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader metadata = pe.GetMetadataReader();
        int row = pe.PEHeaders.MetadataStartOffset + metadata.GetTableMetadataOffset(TableIndex.Assembly);

        // MajorVersion, MinorVersion, BuildNumber and RevisionNumber follow the four bytes of the hash algorithm.
        Span<byte> version = image.AsSpan(row + 4, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(version, majorVersion);
        version[2..].Clear();
    }

    // The project's own copy of an assembly that the surface carries, as the host stages it: the carried
    // System.Text.Json, with another version, in a folder of its own.
    private sealed class StagedCopy : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("WinUIDesignerStagedCopy");

        public StagedCopy(ushort majorVersion)
        {
            byte[] image = File.ReadAllBytes(typeof(JsonSerializerOptions).Assembly.Location);
            SetAssemblyVersion(image, majorVersion);
            FilePath = Path.Combine(_directory.FullName, "System.Text.Json.dll");
            File.WriteAllBytes(FilePath, image);
        }

        public string FilePath { get; }

        public void Dispose()
        {
            try
            {
                _directory.Delete(recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A copy that stays in the temp folder does no harm.
            }
        }
    }
}
