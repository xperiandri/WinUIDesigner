// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

/// <summary>
/// The scopes that resource references are looked up in. <c>{StaticResource}</c> keeps the order it had before the
/// walk was shared with <c>{ThemeResource}</c>: the search parents that Visual Studio sent first.
/// </summary>
[TestClass]
public sealed class ResourceScopeWalkTests
{
    [TestMethod]
    public void EnumerateScopesKeepsStaticResourceOrder()
    {
        // Visual Studio's search parent of the setter is the style's dictionary; the live parent of the page is the grid
        // of the surface; the application comes after the whole chain, then the theme scope. This is the walk that
        // ResourceScopeService.ResolveStaticResource runs: it calls the same function.
        var setter = new FakeElement("Setter");
        var page = new FakeElement("Page");
        var surfaceRoot = new FakeElement("SurfaceRoot");
        var stale = new FakeElement("StaleContainer");
        var application = new FakeDictionary("Application.Resources");
        var themeScope = new FakeDictionary("Theme scope");
        var searchParents = new Dictionary<object, object> { [setter] = page.Resources, [page.Resources] = page };
        var liveParents = new Dictionary<object, object> { [setter] = stale, [page] = surfaceRoot };

        List<FakeDictionary> scopes = ResourceScopeWalk.StaticResourceDictionaries<object, FakeDictionary>(
            setter,
            searchParents,
            liveParents.GetValueOrDefault,
            ThemeResourceThemesTests.DictionaryOf,
            application,
            themeScope
        ).ToList();

        CollectionAssert.AreEqual(
            new[] { setter.Resources, page.Resources, page.Resources, surfaceRoot.Resources, application, themeScope },
            scopes
        );
    }

    [TestMethod]
    public void EnumerateScopesOfAThemeResourceTakesTheLiveParentWhereAStaticResourceTakesTheSearchParent()
    {
        // The same tree as above, for {ThemeResource}: the setter is attached to the stale container now, and Visual
        // Studio's search parent, which it never clears, is not followed. This is the walk of ResolveThemeResource.
        var setter = new FakeElement("Setter");
        var page = new FakeElement("Page");
        var stale = new FakeElement("StaleContainer");
        var application = new FakeDictionary("Application.Resources");
        var themeScope = new FakeDictionary("Theme scope");
        var searchParents = new Dictionary<object, object> { [setter] = page.Resources, [page.Resources] = page };
        var liveParents = new Dictionary<object, object> { [setter] = stale };

        List<FakeDictionary> scopes = ResourceScopeWalk.ThemeResourceDictionaries<object, FakeDictionary>(
            setter,
            searchParents,
            liveParents.GetValueOrDefault,
            ThemeResourceThemesTests.DictionaryOf,
            application,
            themeScope
        ).ToList();

        CollectionAssert.AreEqual(new[] { setter.Resources, stale.Resources, application, themeScope }, scopes);
    }

    [TestMethod]
    public void TheWalkStopsOnACycle()
    {
        var first = new FakeElement("First");
        var second = new FakeElement("Second");
        var parents = new Dictionary<object, object> { [first] = second, [second] = first };

        CollectionAssert.AreEqual(new object[] { first, second }, ResourceScopeWalk.Ancestors<object>(first, parents.GetValueOrDefault).ToArray());
    }

    [TestMethod]
    public void MovedOwnerUsesLiveParentNotStaleSearchParent()
    {
        // Visual Studio never clears a search parent: after an edit moved the border, the old one is still recorded.
        var border = new FakeElement("Border");
        var oldContainer = new FakeElement("OldContainer", ThemeResourceThemes.Light);
        var newContainer = new FakeElement("NewContainer", ThemeResourceThemes.Dark);
        var searchParents = new Dictionary<object, object> { [border] = oldContainer };
        var liveParents = new Dictionary<object, object> { [border] = newContainer };

        object[] walk = ResourceScopeWalk.Ancestors<object>(
            border,
            current => ResourceScopeWalk.LiveParentFirst(current, searchParents, liveParents.GetValueOrDefault)
        ).ToArray();

        CollectionAssert.AreEqual(new object[] { border, newContainer }, walk);
    }

    [TestMethod]
    public void DetachedOwnerUsesTheSearchParent()
    {
        // While Visual Studio builds a document, objects have no live parent yet.
        var setter = new FakeElement("Setter");
        var style = new FakeElement("Style");
        var searchParents = new Dictionary<object, object> { [setter] = style };
        var liveParents = new Dictionary<object, object>();

        object[] walk = ResourceScopeWalk.Ancestors<object>(
            setter,
            current => ResourceScopeWalk.LiveParentFirst(current, searchParents, liveParents.GetValueOrDefault)
        ).ToArray();

        CollectionAssert.AreEqual(new object[] { setter, style }, walk);
    }
}
