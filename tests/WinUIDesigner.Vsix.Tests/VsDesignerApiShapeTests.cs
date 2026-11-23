// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Pins the shape of the members of Visual Studio's closed-source XAML designer that the extension overrides
/// or calls, in this package or in the packages built on it. The designer assemblies differ between Visual Studio
/// versions, so every member has its own test: a failure names exactly the member that differs in the Visual
/// Studio the tests were built with.
/// </summary>
[TestClass]
public sealed class VsDesignerApiShapeTests
{
    private const string _DesignerInstanceManager = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.DesignerInstanceManager";
    private const string _SurfaceProcessContext = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.SurfaceProcessContext";
    private const string _UwpDesignerInstanceBuilderPlatform = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation.UwpDesignerInstanceBuilderPlatform";
    private const string _SurfaceProcessMarkupProvider = "Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents.SurfaceProcessMarkupProvider";
    private const string _XamlCleaner = "Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Documents.XamlModify.XamlCleaner";
    private const string _XamlSceneView = "Microsoft.VisualStudio.DesignTools.XamlSurfaceDesigner.Views.XamlSceneView";
    private const string _XmlNamespace = "Microsoft.VisualStudio.DesignTools.Markup.Metadata.XmlNamespace";
    private const string _XmlElementReference = "Microsoft.VisualStudio.DesignTools.Markup.Xaml.XmlElementReference";
    private const string _XmlNamespaceResolver = "Microsoft.VisualStudio.DesignTools.Markup.IXmlNamespaceResolver";
    private const string _CreateSurfaceRequestInfo = "Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM.CreateSurfaceRequestInfo";
    private const string _UwpInstanceBuilder = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Uwp.UwpInstanceBuilder";
    private const string _LiveNodeProxy = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.LiveNodeProxy";
    private const string _InstanceBuilderContext = "Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.IInstanceBuilderContext";
    private const string _ProtocolHandler = "Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking.IProtocolHandler";
    private const string _SurfaceProcessMarkupProviderContract = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.ISurfaceProcessMarkupProvider";
    private const string _SurfaceProcessDocumentContract = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.ISurfaceProcessDocument";
    private const string _MarkupDocument = "Microsoft.VisualStudio.DesignTools.Markup.IMarkupDocument";
    private const string _UwpSurfaceProcessMarkupProvider = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents.UwpSurfaceProcessMarkupProvider";
    private const string _UwpResourceDictionaryCleanerHandler = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents.XamlModify.UwpResourceDictionaryCleanerHandler";
    private const string _UwpXamlCleanerContext = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents.XamlModify.UwpXamlCleanerContext";
    private const string _UwpDesignerResourceDictionaryBuilder = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation.UwpDesignerResourceDictionaryBuilder";
    private const string _DesignerInstanceBuilderUtility = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.DesignerInstanceBuilderUtility";
    private const string _UwpStandInInfo = "Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.SurfaceIsolation.UwpStandInInfo";
    private const string _WpfHostShadowCopyWorker = "Microsoft.VisualStudio.DesignTools.XamlDesignerHost.Platform.ShadowCopy.WpfHostShadowCopyWorker";
    private const string _DocumentRoot = "Microsoft.VisualStudio.DesignTools.Markup.DocumentModel.IDocumentRoot";
    private const string _DocumentNode = "Microsoft.VisualStudio.DesignTools.Markup.DocumentModel.DocumentNode";
    private const string _DocumentCompositeNode = "Microsoft.VisualStudio.DesignTools.Markup.DocumentModel.DocumentCompositeNode";
    private const string _MarkupUtility = "Microsoft.VisualStudio.DesignTools.Markup.MarkupUtility";
    private const string _DesignerInstanceBuilder = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation.DesignerInstanceBuilder";
    private const string _DesignTimePropertiesHelper = "Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Metadata.DesignTimePropertiesHelper";

    private static readonly Lazy<DesignerAssemblies> _Assemblies = new(DesignerAssemblies.Load);

    [ClassCleanup]
    public static void ReleaseAssemblies()
    {
        if (_Assemblies.IsValueCreated)
        {
            _Assemblies.Value.Dispose();
        }
    }

    [TestMethod]
    public void InstanceManagerLetsDerivedClassesIgnoreProperties()
    {
        MethodDefinition method = RequireMethod(
            _DesignerInstanceManager,
            "ShouldIgnoreProperty",
            "IInstanceBuilderContext", "DocumentCompositeNode", "DocumentNode", "IProperty", "Boolean&"
        );

        AssertOverridable(method);
        Assert.IsTrue(method.Parameters[4].IsOut, $"The last parameter must be an out parameter: {Describe(method)}");
        Assert.AreEqual("Boolean", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
    }

    [TestMethod]
    public void DesignerInstanceBuilderPlatformLetsDerivedClassesChooseTheBuilder()
    {
        // The extension derives from the designer platform, so its override binds to the declaration nearest to
        // that class, which is the one the lookup returns first; the base class is reached when there is none.
        MethodDefinition method = RequireMethod(
            _UwpDesignerInstanceBuilderPlatform,
            "GetInstanceBuilder",
            "IInstanceBuilderContext", "DocumentNode"
        );

        AssertOverridable(method);
    }

    [TestMethod]
    public void DesignerInstanceBuilderPlatformLetsDerivedClassesChooseTheFallbackType()
    {
        MethodDefinition method = RequireMethod(
            _UwpDesignerInstanceBuilderPlatform,
            "GetFallbackTypeInfo",
            "IType", "Boolean"
        );

        AssertOverridable(method);
    }

    [TestMethod]
    public void DesignerInstanceBuilderPlatformCanBeSubclassed()
    {
        TypeDefinition type = RequireType(_UwpDesignerInstanceBuilderPlatform);

        Assert.IsTrue(type.IsPublic, $"{type.FullName} must be public.");
        Assert.IsFalse(type.IsSealed, $"{type.FullName} must not be sealed.");

        MethodDefinition? constructor = type.Methods.FirstOrDefault(method =>
            method.IsConstructor && !method.IsStatic && HasParameters(method, "IProjectContext"));
        Assert.IsNotNull(
            constructor,
            $"{type.FullName} needs a constructor (IProjectContext). Constructors found: {DescribeAll(type.Methods.Where(method => method.IsConstructor && !method.IsStatic))}"
        );
        Assert.IsTrue(IsVisibleToDerivedClasses(constructor), $"The constructor must be public or protected: {Describe(constructor)}");
    }

    [TestMethod]
    public void MarkupProviderLetsDerivedClassesSetTheCleaningResult()
    {
        // The extension calls this method from a derived class; it does not override it.
        MethodDefinition method = RequireMethod(
            _SurfaceProcessMarkupProvider,
            "SetCleaningResult",
            "SurfaceProcessDocument", "XamlResult"
        );

        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
        Assert.IsTrue(IsVisibleToDerivedClasses(method), $"Expected a public or protected method: {Describe(method)}");
    }

    [TestMethod]
    public void MarkupProviderExposesItsCleanerContext()
    {
        MethodDefinition method = RequireMethod(
            _SurfaceProcessMarkupProvider,
            "GetXamlCleanerContext",
            "IProjectContext"
        );

        Assert.IsTrue(method.IsPublic, $"Expected a public method: {Describe(method)}");
        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
    }

    [TestMethod]
    public void XamlCleanerCanBeCalledDirectly()
    {
        MethodDefinition method = RequireMethod(
            _XamlCleaner,
            "Clean",
            "String", "IXamlCleanerContext", "IDocumentContext"
        );

        Assert.IsTrue(method.IsPublic && method.IsStatic, $"Expected a public static method: {Describe(method)}");
        Assert.IsTrue(RequireType(_XamlCleaner).IsPublic, $"{_XamlCleaner} must be public.");
    }

    [TestMethod]
    public void XmlNamespaceExposesItsCondition()
    {
        PropertyDefinition property = RequireProperty(_XmlNamespace, "Condition");

        Assert.AreEqual("String", property.PropertyType.Name, $"Unexpected type of {_XmlNamespace}.Condition.");
        Assert.IsTrue(property.GetMethod is { IsPublic: true, IsStatic: false }, $"{_XmlNamespace}.Condition needs a public instance getter.");
    }

    [TestMethod]
    public void ElementReferenceResolvesPrefixes()
    {
        // XmlElementReference implements GetXmlNamespace publicly in Visual Studio 18 and explicitly in
        // Visual Studio 17.14, so the interface member is the one both versions have. The extension calls the
        // public member in the 18 build and reaches the interface member through XmlElementReferenceExtensions
        // in the 17.14 build.
        MethodDefinition method = RequireMethod(_XmlNamespaceResolver, "GetXmlNamespace", "XmlnsPrefix");

        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
        Assert.AreEqual("XmlNamespace", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
        Assert.Contains(
            @interface => @interface.InterfaceType.FullName == _XmlNamespaceResolver,
            RequireType(_XmlElementReference).Interfaces,
            $"{_XmlElementReference} must implement {_XmlNamespaceResolver}."
        );

        MethodDefinition isIgnorable = RequireMethod(_XmlElementReference, "IsIgnorable", "XmlNamespace");
        Assert.IsTrue(isIgnorable.IsPublic && !isIgnorable.IsStatic, $"Expected a public instance method: {Describe(isIgnorable)}");
    }

    [TestMethod]
    public void SurfaceProcessContextLetsDerivedClassesRestartTheSurface()
    {
        MethodDefinition method = RequireMethod(_SurfaceProcessContext, "RestartAfter", "TimeSpan", "Boolean");

        Assert.IsTrue(IsVisibleToDerivedClasses(method), $"Expected a protected or public method: {Describe(method)}");
        Assert.IsFalse(method.IsStatic, $"Expected an instance method: {Describe(method)}");
    }

    [TestMethod]
    public void SceneViewLetsDerivedClassesCreateTheScrollViewer()
    {
        MethodDefinition method = RequireMethod(_XamlSceneView, "CreateScrollViewer", "Artboard");

        AssertOverridable(method);
    }

    [TestMethod]
    public void CreateSurfaceRequestCanBeSubclassed()
    {
        TypeDefinition type = RequireType(_CreateSurfaceRequestInfo);

        Assert.IsTrue(type.IsPublic, $"{type.FullName} must be public.");
        Assert.IsFalse(type.IsSealed, $"{type.FullName} must not be sealed.");
        Assert.Contains(
            method => method.IsConstructor && !method.IsStatic && !method.HasParameters && IsVisibleToDerivedClasses(method),
            type.Methods,
            $"{type.FullName} needs an accessible parameterless constructor."
        );
    }

    [TestMethod]
    public void InstanceBuilderCanBeSubclassedWithoutArguments()
    {
        // The builder for nodes a namespace policy drops derives from it and passes nothing to its constructor.
        TypeDefinition type = RequireType(_UwpInstanceBuilder);

        Assert.IsTrue(type.IsPublic, $"{type.FullName} must be public.");
        Assert.IsFalse(type.IsSealed, $"{type.FullName} must not be sealed.");
        Assert.Contains(
            method => method.IsConstructor && !method.IsStatic && IsVisibleToDerivedClasses(method) && method.Parameters.All(parameter => parameter.HasDefault),
            type.Methods,
            $"{type.FullName} needs an accessible constructor that can be called without arguments. Constructors found: {DescribeAll(type.Methods.Where(method => method.IsConstructor && !method.IsStatic))}"
        );
    }

    [TestMethod]
    [DataRow("CanInstantiate", "IInstanceBuilderContext,DocumentNode")]
    [DataRow("Instantiate", "IInstanceBuilderContext,LiveNodeProxy")]
    [DataRow("Initialize", "IInstanceBuilderContext,LiveNodeProxy,BuildOrder")]
    [DataRow("ReferencePropertyOrChild", "IInstanceBuilderContext,DocumentNode")]
    [DataRow("UpdateProperty", "IInstanceBuilderContext,DocumentCompositeNode,DocumentNode,DocumentNode,IProperty")]
    [DataRow("UpdateEvent", "IInstanceBuilderContext,DocumentCompositeNode,DocumentNode,DocumentNode,IEvent")]
    [DataRow("UpdateChild", "IInstanceBuilderContext,DocumentCompositeNode,DocumentNode,DocumentNode,Int32")]
    public void InstanceBuilderLetsDerivedClassesReplaceWhatItBuilds(string methodName, string parameterTypes)
    {
        MethodDefinition method = RequireMethod(_UwpInstanceBuilder, methodName, parameterTypes.Split(','));

        AssertOverridable(method);
    }

    [TestMethod]
    public void LiveNodeProxyCanBeInvalidated()
    {
        // An invalid proxy is how a builder tells its caller that no object was created.
        MethodDefinition method = RequireMethod(_LiveNodeProxy, "Invalidate");

        Assert.IsTrue(method.IsPublic && !method.IsStatic, $"Expected a public instance method: {Describe(method)}");
    }

    [TestMethod]
    public void InstanceBuilderContextHandsOutTheProxyOfANode()
    {
        // Later parameters, if any, have defaults: the extension passes the node alone.
        TypeDefinition type = RequireType(_InstanceBuilderContext);
        MethodDefinition[] candidates = type.Methods.Where(method => method.Name == "GetLiveNodeProxy").ToArray();

        Assert.Contains(
            method =>
                method.Parameters.Count >= 1
                && method.Parameters[0].ParameterType.Name == "DocumentNode"
                && method.Parameters.Skip(1).All(parameter => parameter.HasDefault)
                && method.ReturnType.Name == "LiveNodeProxy",
            candidates,
            $"{_InstanceBuilderContext}.GetLiveNodeProxy(DocumentNode) was not found. Methods named GetLiveNodeProxy: {DescribeAll(candidates)}"
        );
    }

    [TestMethod]
    public void ProtocolHandlerSendsARequestOfAnyMessageNumber()
    {
        // The extension sends a message of its own to the surface and awaits the reply.
        MethodDefinition method = RequireMethod(_ProtocolHandler, "SendMessageAsync", "Int32", "Object");

        Assert.HasCount(1, method.GenericParameters, $"Expected one type parameter for the reply: {Describe(method)}");
        Assert.AreEqual("Task`1", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
    }

    [TestMethod]
    public void ProtocolHandlerTellsWhenTheConnectionCloses()
    {
        PropertyDefinition property = RequireProperty(_ProtocolHandler, "CancellationToken");

        Assert.AreEqual("CancellationToken", property.PropertyType.Name, $"Unexpected type of {_ProtocolHandler}.CancellationToken.");
    }

    [TestMethod]
    public void InstanceManagerExposesItsProtocolHandlerAndMarkupProvider()
    {
        PropertyDefinition protocolHandler = RequireProperty(_DesignerInstanceManager, "ProtocolHandler");
        PropertyDefinition markupProvider = RequireProperty(_DesignerInstanceManager, "MarkupProvider");

        Assert.AreEqual("IProtocolHandler", protocolHandler.PropertyType.Name, $"Unexpected type of {_DesignerInstanceManager}.ProtocolHandler.");
        Assert.IsTrue(protocolHandler.GetMethod is { IsStatic: false } getProtocolHandler && IsVisibleToDerivedClasses(getProtocolHandler), $"{_DesignerInstanceManager}.ProtocolHandler needs a getter derived classes can call.");
        Assert.AreEqual("ISurfaceProcessMarkupProvider", markupProvider.PropertyType.Name, $"Unexpected type of {_DesignerInstanceManager}.MarkupProvider.");
        Assert.IsTrue(markupProvider.GetMethod is { IsStatic: false } getMarkupProvider && IsVisibleToDerivedClasses(getMarkupProvider), $"{_DesignerInstanceManager}.MarkupProvider needs a getter derived classes can call.");
    }

    [TestMethod]
    public void MarkupProviderListsTheDocumentsItKnows()
    {
        // Their text is scanned for conditional namespaces before a document is sent to the surface.
        PropertyDefinition registered = RequireProperty(_SurfaceProcessMarkupProviderContract, "RegisteredDocuments");
        PropertyDefinition application = RequireProperty(_SurfaceProcessMarkupProviderContract, "AppXaml");
        PropertyDefinition document = RequireProperty(_SurfaceProcessDocumentContract, "Document");

        Assert.StartsWith("IEnumerable", registered.PropertyType.Name, $"Unexpected type of {_SurfaceProcessMarkupProviderContract}.RegisteredDocuments.");
        Assert.AreEqual("ISurfaceProcessDocument", application.PropertyType.Name, $"Unexpected type of {_SurfaceProcessMarkupProviderContract}.AppXaml.");
        Assert.AreEqual("IMarkupDocument", document.PropertyType.Name, $"Unexpected type of {_SurfaceProcessDocumentContract}.Document.");
        Assert.Contains(
            implemented => implemented.InterfaceType.Name == "IDocumentRoot",
            RequireType(_MarkupDocument).Interfaces,
            $"{_MarkupDocument} must be a document root."
        );
    }

    [TestMethod]
    [DataRow("ParseCompleted")]
    [DataRow("Disposing")]
    public void MarkupDocumentTellsWhenAnEditWasParsedAndWhenItGoes(string eventName)
    {
        // After each edit the extension looks at what the namespace policies drop, and stops when the document goes.
        EventDefinition documentEvent = RequireEvent(_MarkupDocument, eventName);

        Assert.AreEqual("EventHandler", documentEvent.EventType.Name, $"Unexpected type of {_MarkupDocument}.{eventName}.");
    }

    [TestMethod]
    public void DocumentRootAnnouncesTheChangeOfANode()
    {
        // The change that builds a node again is announced the way every other change of the document is.
        MethodDefinition method = RequireMethod(_DocumentRoot, "OnNodeChange", "DocumentNode", "DocumentNodeChange");

        Assert.AreEqual("Void", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
        Assert.IsTrue(method.IsPublic && !method.IsStatic, $"Expected a public instance method: {Describe(method)}");
    }

    [TestMethod]
    public void MarkupUtilityCreatesTheChangeThatBuildsANodeAgain()
    {
        MethodDefinition method = RequireMethod(_MarkupUtility, "CreateRebuild", "DocumentNode");

        Assert.AreEqual("DocumentNodeChange", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
        Assert.IsTrue(method.IsPublic && method.IsStatic, $"Expected a public static method: {Describe(method)}");
    }

    [TestMethod]
    [DataRow("DescendantNodes", "IEnumerable`1")]
    [DataRow("IsInDocument", "Boolean")]
    [DataRow("Parent", "DocumentCompositeNode")]
    public void DocumentNodeCanBeWalked(string propertyName, string propertyType)
    {
        // The elements and the properties of a document are visited to see which of them the policies drop.
        PropertyDefinition property = RequireProperty(_DocumentNode, propertyName);

        Assert.AreEqual(propertyType, property.PropertyType.Name, $"Unexpected type of {_DocumentNode}.{propertyName}.");
        Assert.IsTrue(property.GetMethod is { IsPublic: true, IsStatic: false }, $"{_DocumentNode}.{propertyName} needs a public instance getter.");
    }

    [TestMethod]
    public void CompositeNodeListsItsProperties()
    {
        PropertyDefinition properties = RequireProperty(_DocumentCompositeNode, "Properties");
        TypeDefinition dictionary = RequireType($"{properties.PropertyType.Namespace}.{properties.PropertyType.Name}");

        Assert.Contains(
            implemented => implemented.InterfaceType.FullName.StartsWith("System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<", StringComparison.Ordinal),
            dictionary.Interfaces,
            $"{dictionary.FullName} must list its properties as key and value pairs."
        );
    }

    private static EventDefinition RequireEvent(string typeName, string eventName)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        for (TypeDefinition? type = RequireType(typeName); type is not null; type = assemblies.FindBaseType(type))
        {
            EventDefinition? definition = type.Events.FirstOrDefault(candidate => candidate.Name == eventName);
            if (definition is not null)
            {
                return definition;
            }
        }

        Assert.Fail($"Event {typeName}.{eventName} was not found in the designer assemblies of {assemblies.InstallRoot}.");
        return null!;
    }

    // The surface merges a dictionary named through ResourceDictionary.Source from the copy Visual Studio prepares
    // for it. The tests below pin what that relies on: who prepares the copy, how its URI looks, and how the source
    // is passed on: as the attribute of prepared XAML, and as a property in construction actions.

    [TestMethod]
    public void MarkupProviderPreparesTheDictionariesADocumentMerges()
    {
        MethodDefinition method = RequireMethod(_SurfaceProcessMarkupProvider, "RegisterRelatedDocument", "String", "IDocumentContext", "Boolean");

        Assert.IsTrue(method.IsPublic && !method.IsStatic, $"Expected a public instance method: {Describe(method)}");
        Assert.AreEqual("SurfaceProcessDocumentResult", method.ReturnType.Name, $"Unexpected return type: {Describe(method)}");
    }

    [TestMethod]
    public void PreparedCopyIsNamedWithTheDesignTimeMarker()
    {
        // "~vs]~{project item id}~{file name}": the surface reads the document id after the marker.
        MethodDefinition method = RequireMethod(_SurfaceProcessMarkupProvider, "CreateDesignTimeFileName", "IDocumentLocator");

        Assert.IsTrue(LoadsString(method, "~vs]~"), $"{Describe(method)} no longer names prepared copies with \"~vs]~\".");
    }

    [TestMethod]
    public void UwpMarkupProviderWritesAPreparedCopyWhereItsUriPoints()
    {
        // ms-appx:///Folder/~vs]~id~File.xaml is written to Folder\~vs]~id~File.xaml below the shadow cache.
        MethodDefinition uri = RequireMethod(_UwpSurfaceProcessMarkupProvider, "CreateDesignTimeSurfaceUri", "IMarkupDocument");
        MethodDefinition path = RequireMethod(_UwpSurfaceProcessMarkupProvider, "GetShadowCachePath", "SurfaceProcessDocument");

        Assert.AreEqual(_UwpSurfaceProcessMarkupProvider, uri.DeclaringType.FullName, $"The UWP provider no longer creates the design-time URI itself: {Describe(uri)}");
        Assert.AreEqual(_UwpSurfaceProcessMarkupProvider, path.DeclaringType.FullName, $"The UWP provider no longer chooses the path of a prepared copy itself: {Describe(path)}");
    }

    [TestMethod]
    public void HostShadowCopyWorkerHasNoApplicationXbf()
    {
        // With an App.xbf, Visual Studio's cleaner keeps the runtime source of an unchanged dictionary instead of
        // preparing a copy. The WinUI worker inherits this answer.
        PropertyDefinition property = RequireProperty(_WpfHostShadowCopyWorker, "HasAppXbf");
        MethodDefinition getter = property.GetMethod;

        Assert.IsNotNull(getter, $"{_WpfHostShadowCopyWorker}.HasAppXbf needs a getter.");
        Assert.IsTrue(
            getter.HasBody && getter.Body.Instructions.Select(instruction => instruction.OpCode).SequenceEqual(new[] { OpCodes.Ldc_I4_0, OpCodes.Ret }),
            $"{_WpfHostShadowCopyWorker}.HasAppXbf no longer returns false.");
    }

    [TestMethod]
    public void CleanerWritesTheSourceOfADictionaryAsADesignTimeProperty()
    {
        MethodDefinition method = RequireMethod(_UwpResourceDictionaryCleanerHandler, "HandleAttribute", "XamlCleanerAttributeData", "IXmlCleaner");

        Assert.IsTrue(LoadsString(method, "DT.ResourceDictionarySource"), $"{Describe(method)} no longer writes DT.ResourceDictionarySource.");
        Assert.IsTrue(Calls(method, "RegisterRelatedDocument"), $"{Describe(method)} no longer prepares the dictionary it names.");
        Assert.IsTrue(Calls(method, "get_DesignTimeSurfaceUri"), $"{Describe(method)} no longer passes the design-time URI of the prepared copy.");
    }

    [TestMethod]
    public void CleanerPutsDesignTimePropertiesIntoTheXSurfUwpNamespace()
    {
        PropertyDefinition property = RequireProperty(_UwpXamlCleanerContext, "DesignTimeNamespaceUri");

        Assert.IsTrue(LoadsString(property.GetMethod, "using:XSurfUwp"), $"{_UwpXamlCleanerContext}.DesignTimeNamespaceUri is no longer \"using:XSurfUwp\".");
    }

    [TestMethod]
    public void ConstructionActionsSetTheSourceOfADictionaryThroughTheDesignTimeProperty()
    {
        MethodDefinition update = RequireMethod(_UwpDesignerResourceDictionaryBuilder, "UpdateProperty", "IInstanceBuilderContext", "DocumentCompositeNode", "DocumentNode", "DocumentNode", "IProperty");
        MethodDefinition set = RequireMethod(_DesignerInstanceBuilderUtility, "SetDesignTimeResourceDictionarySource", "DesignerInstanceBuilderContext", "DocumentNode", "DocumentNode", "IProperty", "Boolean");
        MethodDefinition validate = RequireMethod(_DesignerInstanceBuilderUtility, "TryGetAndValidateResourceDictionaryForSource", "DesignerInstanceBuilderContext", "DocumentNode", "ISurfaceProcessDocument&");

        Assert.IsTrue(Calls(update, "SetDesignTimeResourceDictionarySource"), $"{Describe(update)} no longer sets the design-time source.");
        Assert.IsTrue(Calls(set, "TryGetDesignTimeResourceDictionarySource"), $"{Describe(set)} no longer looks up the design-time source.");
        Assert.IsTrue(Calls(validate, "RegisterRelatedDocument"), $"{Describe(validate)} no longer prepares the dictionary through the markup provider.");
        Assert.AreEqual(
            "DT.ResourceDictionarySource",
            RequireType(_UwpStandInInfo).Fields.Single(field => field.Name == "ResourceDictionarySourceHandlePropertyName").Constant,
            $"{_UwpStandInInfo}.ResourceDictionarySourceHandlePropertyName changed.");
    }

    [TestMethod]
    public void DesignerInstanceBuilderPlatformCreatesItsBuilderForTheXSurfUwpOwner()
    {
        // The owner that the builder writes for a design-time property: "ResourceDictionarySource:XSurfUwp.DT".
        TypeDefinition type = RequireType(_UwpDesignerInstanceBuilderPlatform);
        MethodDefinition[] constructors = type.Methods.Where(method => method.IsConstructor && !method.IsStatic).ToArray();

        Assert.IsTrue(constructors.Any(constructor => LoadsString(constructor, "XSurfUwp.DT")), $"No constructor of {_UwpDesignerInstanceBuilderPlatform} creates the design-time builder for \"XSurfUwp.DT\".");
    }

    [TestMethod]
    [DataRow("SetDesignTimeResourceDictionarySource", "DesignerInstanceBuilderContext,DocumentNode,DocumentNode,IProperty,Boolean")]
    [DataRow("ClearDesignTimeResourceDictionarySource", "DesignerInstanceBuilderContext,DocumentNode,IProperty")]
    public void ConstructionActionsSerializeTheDictionarySourceThroughThePlatform(string methodName, string parameterTypes)
    {
        // The wire name is whatever the platform makes of the design-time property, which is what IsSourceProperty reads.
        MethodDefinition method = RequireMethod(_DesignerInstanceBuilderUtility, methodName, parameterTypes.Split(','));

        Assert.IsTrue(Calls(method, "get_ResourceDictionarySourceProperty"), $"{Describe(method)} no longer uses the design-time property of the dictionary source.");
        Assert.IsTrue(Calls(method, "SerializeProperty"), $"{Describe(method)} no longer lets the platform serialize the property.");
    }

    [TestMethod]
    public void PlatformGivesADesignTimePropertyTheDesignTimeOwner()
    {
        // The UWP platform hands the property to the DesignerInstanceBuilder it created for "XSurfUwp.DT", which names
        // a member flagged as a design-time property with that owner and any other with its declaring type.
        MethodDefinition platform = RequireMethod(_UwpDesignerInstanceBuilderPlatform, "SerializeProperty", "IProperty");
        MethodDefinition builder = RequireMethod(_DesignerInstanceBuilder, "SerializeProperty", "IProperty", "String");

        Assert.AreEqual(_UwpDesignerInstanceBuilderPlatform, platform.DeclaringType.FullName, $"The platform no longer serializes properties itself: {Describe(platform)}");
        Assert.IsTrue(CallsOn(platform, _DesignerInstanceBuilder, "SerializeProperty"), $"{Describe(platform)} no longer hands the property to the DesignerInstanceBuilder.");
        Assert.IsTrue(Calls(builder, "HasFlag"), $"{Describe(builder)} no longer looks at the member type of the property.");
        Assert.IsTrue(builder.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldfld && instruction.Operand is FieldReference { Name: "designTimeTypeName" }), $"{Describe(builder)} no longer names a design-time property with its design-time owner.");
        Assert.IsTrue(Calls(builder, "FormatFullMemberName"), $"{Describe(builder)} no longer formats \"Member:Owner\".");
    }

    [TestMethod]
    public void DictionarySourceIsNotMappedToARuntimePropertyBySerialization()
    {
        // The platform replaces a design-time property by the runtime property it stands for. It does that for
        // DesignStyle and DesignSource only; the dictionary source keeps its own name, which the surface handles.
        TypeDefinition helper = RequireType(_DesignTimePropertiesHelper);
        MethodDefinition initializer = helper.Methods.Single(method => method.IsConstructor && method.IsStatic);

        Assert.IsFalse(Calls(initializer, "get_ResourceDictionarySourceProperty"), $"{_DesignTimePropertiesHelper} maps the dictionary source to a runtime property now.");
    }

    private static bool LoadsString(MethodDefinition method, string value)
    {
        return method.HasBody && method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr && (string)instruction.Operand == value);
    }

    private static bool Calls(MethodDefinition method, string methodName)
    {
        return method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called && called.Name == methodName);
    }

    private static bool CallsOn(MethodDefinition method, string typeName, string methodName)
    {
        return method.HasBody && method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference called && called.Name == methodName && called.DeclaringType.FullName == typeName);
    }

    private static TypeDefinition RequireType(string fullName)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        TypeDefinition? type = assemblies.FindType(fullName);
        Assert.IsNotNull(type, $"Type {fullName} was not found in the designer assemblies of {assemblies.InstallRoot}.");
        return type;
    }

    /// <summary>
    /// Finds a method by name and by the simple names of its parameter types, on the type or on a base type:
    /// a member the extension overrides may be declared anywhere up the hierarchy.
    /// </summary>
    private static MethodDefinition RequireMethod(string typeName, string methodName, params string[] parameterTypes)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        var candidates = new List<MethodDefinition>();
        for (TypeDefinition? type = RequireType(typeName); type is not null; type = assemblies.FindBaseType(type))
        {
            foreach (MethodDefinition method in type.Methods.Where(method => method.Name == methodName))
            {
                if (HasParameters(method, parameterTypes))
                {
                    return method;
                }

                candidates.Add(method);
            }
        }

        Assert.Fail(
            $"{typeName}.{methodName}({string.Join(", ", parameterTypes)}) was not found in the designer assemblies of {assemblies.InstallRoot}. " +
            $"Methods named {methodName}: {DescribeAll(candidates)}"
        );
        return null!;
    }

    private static PropertyDefinition RequireProperty(string typeName, string propertyName)
    {
        DesignerAssemblies assemblies = _Assemblies.Value;
        for (TypeDefinition? type = RequireType(typeName); type is not null; type = assemblies.FindBaseType(type))
        {
            PropertyDefinition? property = type.Properties.FirstOrDefault(candidate => candidate.Name == propertyName);
            if (property is not null)
            {
                return property;
            }
        }

        Assert.Fail($"Property {typeName}.{propertyName} was not found in the designer assemblies of {assemblies.InstallRoot}.");
        return null!;
    }

    private static void AssertOverridable(MethodDefinition method)
    {
        Assert.IsTrue(method.IsVirtual && !method.IsFinal, $"Expected a virtual method that is not sealed: {Describe(method)}");
        Assert.IsTrue(IsVisibleToDerivedClasses(method), $"Expected a public or protected method: {Describe(method)}");
    }

    private static bool IsVisibleToDerivedClasses(MethodDefinition method)
    {
        return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
    }

    private static bool HasParameters(MethodDefinition method, params string[] parameterTypes)
    {
        return method.Parameters.Count == parameterTypes.Length
            && method.Parameters.Select(parameter => parameter.ParameterType.Name).SequenceEqual(parameterTypes, StringComparer.Ordinal);
    }

    private static string DescribeAll(IEnumerable<MethodDefinition> methods)
    {
        string description = string.Join("; ", methods.Select(Describe));
        return description.Length == 0 ? "none" : description;
    }

    private static string Describe(MethodDefinition method)
    {
        string access = method.IsPublic ? "public"
            : method.IsFamily ? "protected"
            : method.IsFamilyOrAssembly ? "protected internal"
            : method.IsAssembly ? "internal"
            : method.IsFamilyAndAssembly ? "private protected"
            : "private";
        string kind = method.IsStatic ? " static"
            : method.IsAbstract ? " abstract"
            : method.IsVirtual && method.IsFinal ? " sealed"
            : method.IsVirtual ? " virtual"
            : string.Empty;
        string parameters = string.Join(", ", method.Parameters.Select(parameter => parameter.ParameterType.Name));
        return $"{access}{kind} {method.ReturnType.Name} {method.DeclaringType.FullName}.{method.Name}({parameters})";
    }

    /// <summary>
    /// The designer assemblies of one Visual Studio installation, opened for metadata inspection only.
    /// </summary>
    private sealed class DesignerAssemblies : IDisposable
    {
        private const string _InstallRootVariable = "WINUIDESIGNER_VS_INSTALL_ROOT";
        private const string _InstallRootMetadata = "VsInstallRoot";

        private readonly List<ModuleDefinition> _modules = [];

        private DesignerAssemblies(string installRoot)
        {
            InstallRoot = installRoot;
        }

        public string InstallRoot { get; }

        public static DesignerAssemblies Load()
        {
            string installRoot = FindInstallRoot();
            var assemblies = new DesignerAssemblies(installRoot);
            foreach (string folder in new[] { "PrivateAssemblies", "PublicAssemblies" })
            {
                string directory = Path.Combine(installRoot, "Common7", "IDE", folder);
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string path in Directory.EnumerateFiles(directory, "Microsoft.VisualStudio.DesignTools.*.dll"))
                {
                    try
                    {
                        assemblies._modules.Add(ModuleDefinition.ReadModule(path, new ReaderParameters { ReadingMode = ReadingMode.Deferred }));
                    }
                    catch (BadImageFormatException)
                    {
                        // Not a managed assembly; nothing the extension derives from lives there.
                    }
                }
            }

            if (assemblies._modules.Count == 0)
            {
                throw new FileNotFoundException(
                    $"No designer assemblies (Microsoft.VisualStudio.DesignTools.*.dll) were found under '{installRoot}'. " +
                    $"Set {_InstallRootVariable} to the root of a Visual Studio installation."
                );
            }

            return assemblies;
        }

        public TypeDefinition? FindType(string fullName)
        {
            foreach (ModuleDefinition module in _modules)
            {
                TypeDefinition? type = module.GetType(fullName);
                if (type is not null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the base type when it is defined in a designer assembly; the walk ends at framework types.
        /// </summary>
        public TypeDefinition? FindBaseType(TypeDefinition type)
        {
            return type.BaseType is null ? null : FindType(type.BaseType.GetElementType().FullName);
        }

        public void Dispose()
        {
            foreach (ModuleDefinition module in _modules)
            {
                module.Dispose();
            }

            _modules.Clear();
        }

        private static string FindInstallRoot()
        {
            string? configured = Environment.GetEnvironmentVariable(_InstallRootVariable);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured!;
            }

            // Visual Studio's MSBuild defines VsInstallRoot; the test project embeds it at build time.
            string? embedded = typeof(VsDesignerApiShapeTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == _InstallRootMetadata)?.Value;
            if (!string.IsNullOrWhiteSpace(embedded))
            {
                return embedded!;
            }

            throw new DirectoryNotFoundException(
                $"The Visual Studio installation is unknown: the tests were built without VsInstallRoot. " +
                $"Build them with Visual Studio's MSBuild or set {_InstallRootVariable}."
            );
        }
    }
}
