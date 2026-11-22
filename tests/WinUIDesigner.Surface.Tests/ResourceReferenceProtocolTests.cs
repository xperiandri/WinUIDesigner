// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class ResourceReferenceProtocolTests
{
    // The action that failed the construction of AutoSuggestAddress.xaml on 2026-11-22, as the surface logged it.
    private const string _LoggedThemeReferenceCreation = """{"Action":3,"Id":555,"LiveHandle":-39,"FallbackTypeName":"","TypeName":"System.Windows.ThemeResourceExtension"}""";

    // The member that Visual Studio sets on both references: the property of the proxy of System.Object.
    private const string _ResourceKeyProperty = "ResourceKey:System.Object, System.Runtime, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

    [TestMethod]
    public void ThemeResourceProtocolNameIsTheOneVisualStudioSends()
    {
        Assert.AreEqual(typeof(ThemeResourceReference), ResourceReferenceProtocol.GetReferenceType("System.Windows.ThemeResourceExtension"));
        Assert.AreEqual(typeof(ThemeResourceReference), ResourceReferenceProtocol.GetReferenceType(" System.Windows.ThemeResourceExtension , UwpSurface"));
        Assert.AreEqual(typeof(StaticResourceReference), ResourceReferenceProtocol.GetReferenceType("System.Windows.StaticResourceExtension"));
    }

    [TestMethod]
    [DataRow("System.Windows.CustomResourceExtension")]
    [DataRow("Microsoft.UI.Xaml.ThemeResourceExtension")]
    [DataRow("System.Windows.ThemeResourceExtensionX")]
    [DataRow("")]
    [DataRow(null)]
    public void OtherNamesAreNotResourceReferences(string? typeName) => Assert.IsNull(ResourceReferenceProtocol.GetReferenceType(typeName));

    [TestMethod]
    [DataRow(typeof(StaticResourceReference))]
    [DataRow(typeof(ThemeResourceReference))]
    public void BothReferencesExposeAWritableObjectResourceKey(Type referenceType)
    {
        // Visual Studio sets the inherited proxy member by name; the surface finds it on the target type first.
        PropertyInfo? property = referenceType.GetProperty("ResourceKey", BindingFlags.Public | BindingFlags.Instance);

        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(object), property.PropertyType);
        Assert.IsTrue(property.CanWrite);
    }

    [TestMethod]
    public void TheLoggedCreationOfAThemeReferenceIsRecognized()
    {
        var create = (CreateInstanceAction)XamlActionJsonSerializer.Deserialize([_LoggedThemeReferenceCreation]).Single();

        Assert.AreEqual(-39, create.LiveHandle);
        Assert.AreEqual(typeof(ThemeResourceReference), ResourceReferenceProtocol.GetReferenceType(create.TypeName));
        Assert.IsTrue(string.IsNullOrEmpty(create.FallbackTypeName), "Visual Studio sends no fallback for a reference.");
    }

    [TestMethod]
    public void TheKeyOfAThemeReferenceRoundTripsThroughVisualStudiosSerializer()
    {
        XamlAction[] sent =
        [
            new CreateInstanceAction(-39, ThemeResourceReference.ProtocolTypeName),
            new TypeConvertInstanceAction(-41, "System.String, System.Runtime", "SystemFillColorCriticalBrush"),
            new SetPropertyAction(-39, _ResourceKeyProperty, -41, 0),
            new SetPropertyAction(-37, "Value:Microsoft.UI.Xaml.Setter, Microsoft.WinUI", -39, 0),
        ];

        IList<XamlAction> received = XamlActionJsonSerializer.Deserialize(XamlActionJsonSerializer.Serialize(sent));

        Assert.HasCount(4, received);
        Assert.AreEqual(typeof(ThemeResourceReference), ResourceReferenceProtocol.GetReferenceType(((CreateInstanceAction)received[0]).TypeName));
        Assert.AreEqual("SystemFillColorCriticalBrush", ((TypeConvertInstanceAction)received[1]).Value);
        var key = (SetPropertyAction)received[2];
        Assert.AreEqual(-39, key.ParentHandle);
        Assert.AreEqual(-41, key.PropertyValueHandle);
        Assert.StartsWith("ResourceKey:", key.FullPropertyName);
        Assert.AreEqual(-39, ((SetPropertyAction)received[3]).PropertyValueHandle);
    }

    [TestMethod]
    public void TheProtocolNamesAreTheConstantsOfVisualStudiosBuilder()
    {
        // Read from the Visual Studio of the build: 18 locally, 17.14 on the CI.
        using var runtimeHost = new RuntimeHostMetadata();
        TypeDefinitionHandle builder = runtimeHost.FindUwpBuilder("ResourceReferenceBuilder");

        Assert.AreEqual(ThemeResourceReference.ProtocolTypeName, runtimeHost.GetStringConstant(builder, "ThemeResourceExtensionType"));
        Assert.AreEqual(StaticResourceReference.ProtocolTypeName, runtimeHost.GetStringConstant(builder, "StaticResourceExtensionType"));
    }

    [TestMethod]
    public void VisualStudioRebuildsAResourceReferenceAfterEveryChange()
    {
        // The surface replaces the handle of a reference by its value, so Visual Studio must never address it again. That
        // holds while its builder rebuilds: ResourceReferenceBuilder derives from RebuildBuilder, whose Invalidate
        // returns Rebuild whatever changed.
        using var runtimeHost = new RuntimeHostMetadata();
        TypeDefinitionHandle builder = runtimeHost.FindUwpBuilder("ResourceReferenceBuilder");
        TypeDefinitionHandle rebuildBuilder = runtimeHost.FindUwpBuilder("RebuildBuilder");

        EntityHandle baseType = runtimeHost.Metadata.GetTypeDefinition(builder).BaseType;
        Assert.AreEqual(HandleKind.TypeDefinition, baseType.Kind);
        Assert.AreEqual(rebuildBuilder, (TypeDefinitionHandle)baseType);

        // ldc.i4.<Rebuild>; ret, without the nops of a debug build.
        byte[] body = runtimeHost.GetMethodBody(rebuildBuilder, "Invalidate").Where(opcode => opcode != 0x00).ToArray();
        CollectionAssert.AreEqual(new byte[] { (byte)(0x16 + (int)InvalidationType.Rebuild), 0x2A }, body);
    }

    /// <summary>
    /// Visual Studio's <c>Microsoft.VisualStudio.DesignTools.RuntimeHost.dll</c>, copied next to the tests by the build, read
    /// as metadata only: loading its builders would need the rest of Visual Studio.
    /// </summary>
    private sealed class RuntimeHostMetadata : IDisposable
    {
        private const string _UwpBuilders = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Uwp";

        private readonly FileStream _stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.DesignTools.RuntimeHost.dll"));
        private readonly PEReader _image;

        public RuntimeHostMetadata()
        {
            _image = new PEReader(_stream);
            Metadata = _image.GetMetadataReader();
        }

        public MetadataReader Metadata { get; }

        public TypeDefinitionHandle FindUwpBuilder(string name)
        {
            foreach (TypeDefinitionHandle handle in Metadata.TypeDefinitions)
            {
                TypeDefinition type = Metadata.GetTypeDefinition(handle);
                if (Metadata.GetString(type.Name) == name && Metadata.GetString(type.Namespace) == _UwpBuilders)
                {
                    return handle;
                }
            }

            throw new AssertFailedException($"{_UwpBuilders}.{name} is not in the RuntimeHost of this Visual Studio.");
        }

        public string? GetStringConstant(TypeDefinitionHandle type, string fieldName)
        {
            FieldDefinition field = Metadata.GetTypeDefinition(type).GetFields()
                .Select(Metadata.GetFieldDefinition)
                .Single(candidate => Metadata.GetString(candidate.Name) == fieldName);
            Constant constant = Metadata.GetConstant(field.GetDefaultValue());
            return Metadata.GetBlobReader(constant.Value).ReadConstant(constant.TypeCode) as string;
        }

        public byte[] GetMethodBody(TypeDefinitionHandle type, string methodName)
        {
            MethodDefinition method = Metadata.GetTypeDefinition(type).GetMethods()
                .Select(Metadata.GetMethodDefinition)
                .Single(candidate => Metadata.GetString(candidate.Name) == methodName);
            return _image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes() ?? [];
        }

        public void Dispose()
        {
            _image.Dispose();
            _stream.Dispose();
        }
    }
}
