// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Mono.Cecil;
using Mono.Cecil.Cil;

/// <summary>
/// Pins how the surface gets the conditions the user forces into the rewrite of prepared XAML: the overrides arrive with
/// the CreateSurface request, the service keeps the ones of the latest request, and every rewrite asks them before it
/// evaluates anything. The service needs WinUI and cannot run in a test, so the compiled methods are read, the way the
/// order of the first await of the CreateSurface handler is checked next to it.
/// </summary>
[TestClass]
public sealed class ConditionalXamlSurfaceWiringTests
{
    private const string _Service = "WinUIDesigner.Surface.Services.SurfaceService";
    private const string _LatestOptions = "_latestDesignTimeOptions";

    [TestMethod]
    public void EveryRewriteOfPreparedXamlIsConfiguredWithTheConditionPolicy()
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(SurfaceBuild.GetAssemblyPath());

        Assert.Contains("ConfigureConditionalNamespacePolicy", GetCalls(FindMethod(surface, "CreateNamespaceRewriteOptions")));
    }

    [TestMethod]
    public void TheConditionPolicyAsksTheOverridesOfTheRequestBeforeItEvaluatesAnything()
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(SurfaceBuild.GetAssemblyPath());

        List<string> calls = GetCalls(FindMethod(surface, "ConfigureConditionalNamespacePolicy"));

        // The evaluator the rewrite uses is composed of the overrides and the probes of the surface.
        Assert.Contains("GetConditionOverrides", calls, "The rewrite does not ask the overrides the request carried.");
        Assert.Contains("CreateEvaluator", calls, "The rewrite evaluates conditions without the overrides first.");
    }

    [TestMethod]
    public void TheOverridesOfTheLatestRequestAreTheOnesTheRewriteAsks()
    {
        using AssemblyDefinition surface = AssemblyDefinition.ReadAssembly(SurfaceBuild.GetAssemblyPath());

        // The request stores its options in the field that the overrides are read from.
        MethodDefinition store = FindMethod(surface, "StoreDesignTimeOptions");
        Assert.Contains(
            instruction => instruction.OpCode.Code == Code.Stfld && ((FieldReference)instruction.Operand).Name == _LatestOptions,
            store.Body.Instructions,
            "The request does not store its options as the latest ones."
        );

        MethodDefinition get = FindMethod(surface, "GetConditionOverrides");
        Assert.Contains(
            instruction => instruction.OpCode.Code == Code.Ldfld && ((FieldReference)instruction.Operand).Name == _LatestOptions,
            get.Body.Instructions,
            "The overrides are not read from the latest options."
        );
        Assert.Contains("get_ConditionOverrides", GetCalls(get));
    }

    private static MethodDefinition FindMethod(AssemblyDefinition surface, string name)
    {
        TypeDefinition? service = surface.MainModule.GetType(_Service);
        Assert.IsNotNull(service);
        return service.Methods.Single(method => method.Name == name);
    }

    private static List<string> GetCalls(MethodDefinition method)
    {
        return method.Body.Instructions
            .Where(instruction => instruction.OpCode.Code is Code.Call or Code.Callvirt)
            .Select(instruction => ((MethodReference)instruction.Operand).Name)
            .ToList();
    }
}
