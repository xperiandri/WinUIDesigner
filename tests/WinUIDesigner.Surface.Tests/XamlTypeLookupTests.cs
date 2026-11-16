// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class XamlTypeLookupTests
{
    // A nested type, like SafeArea.InsetMask of the Uno Toolkit.
    private enum NestedMask
    {
        None,
        All,
    }

    private static Func<Type, string?> Providers(params (Type Type, string Metadata)[] known)
    {
        var metadata = known.ToDictionary(item => item.Type, item => item.Metadata);
        return type => metadata.TryGetValue(type, out string? found) ? found : null;
    }

    [TestMethod]
    public void NestedTypeIsKnownByANameWithADot()
    {
        // Type.FullName reads "XamlTypeLookupTests+NestedMask"; the providers write "XamlTypeLookupTests.NestedMask".
        Assert.AreEqual("XamlTypeLookupTests+NestedMask", typeof(NestedMask).FullName);
        Assert.AreEqual("XamlTypeLookupTests.NestedMask", XamlTypeLookup.GetXamlName(typeof(NestedMask)));
    }

    [TestMethod]
    public void TypeWithoutANestingKeepsItsFullName()
    {
        Assert.AreEqual("System.String", XamlTypeLookup.GetXamlName(typeof(string)));
    }

    [TestMethod]
    public void ConstructedGenericTypeHasNoName()
    {
        Assert.IsNull(XamlTypeLookup.GetXamlName(typeof(List<string>)));
    }

    [TestMethod]
    public void NestedTypeIsFoundByTheNameWhenNoProviderKnowsTheTypeItself()
    {
        string? requested = null;

        string? found = XamlTypeLookup.Find<string>(
            typeof(NestedMask),
            [Providers()],
            name =>
            {
                requested = name;
                return "metadata of " + name;
            });

        Assert.AreEqual("XamlTypeLookupTests.NestedMask", requested);
        Assert.AreEqual("metadata of XamlTypeLookupTests.NestedMask", found);
    }

    [TestMethod]
    public void ProviderThatKnowsTheTypeIsPreferredToTheLookupByName()
    {
        string? found = XamlTypeLookup.Find<string>(
            typeof(NestedMask),
            [Providers(), Providers((typeof(NestedMask), "from the second provider"))],
            _ => throw new AssertFailedException("The name must not be asked for when a provider knows the type."));

        Assert.AreEqual("from the second provider", found);
    }

    [TestMethod]
    public void FirstProviderThatKnowsTheTypeWins()
    {
        string? found = XamlTypeLookup.Find<string>(
            typeof(NestedMask),
            [Providers((typeof(NestedMask), "first")), Providers((typeof(NestedMask), "second"))],
            _ => null);

        Assert.AreEqual("first", found);
    }

    [TestMethod]
    public void GenericTypeNoProviderKnowsIsNotLookedUpByName()
    {
        string? found = XamlTypeLookup.Find<string>(
            typeof(List<string>),
            [Providers()],
            _ => throw new AssertFailedException("A constructed generic type has no name to ask for."));

        Assert.IsNull(found);
    }

    [TestMethod]
    public void NothingIsFoundWhenNeitherTheTypeNorTheNameIsKnown()
    {
        Assert.IsNull(XamlTypeLookup.Find<string>(typeof(NestedMask), [Providers()], _ => null));
    }
}
