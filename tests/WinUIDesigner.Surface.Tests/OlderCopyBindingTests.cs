// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

#if NET9_0_OR_GREATER
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// The runtime rules that the surface relies on when its own copy of an assembly serves a reference to a newer
/// version of it. System.Text.Json stands in for a projection: every test host carries it, as the surface carries its
/// projections. <see cref="PersistedAssemblyBuilder"/>, which writes the probes, exists from .NET 9 on.
/// </summary>
[TestClass]
public sealed class OlderCopyBindingTests
{
    [TestMethod]
    public void TheRuntimeDoesNotBindANewerReferenceToTheCarriedCopy()
    {
        Func<string> probe = CreateProbe("OlderCopyBindingProbe.Unserved", 98);

        Assert.Throws<IOException>(() => probe());
    }

    [TestMethod]
    public void TheCarriedCopyThatAHandlerReturnsServesANewerReference()
    {
        Func<string> probe = CreateProbe("OlderCopyBindingProbe.Served", 99);
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

    // An assembly whose method returns the assembly-qualified name of JsonSerializerOptions, with its reference to
    // System.Text.Json raised to a version that no runtime carries.
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
}
#endif
