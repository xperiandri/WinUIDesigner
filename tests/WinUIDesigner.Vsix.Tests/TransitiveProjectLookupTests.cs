// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

/// <summary>
/// Finding the project that a <c>ms-appx:///Assembly/...</c> source names when Visual Studio does not: the application
/// of the Ecierge solution references its libraries, they reference the shared one, and only the application's
/// direct references are looked at by Visual Studio.
/// </summary>
[TestClass]
public sealed class TransitiveProjectLookupTests
{
    [TestMethod]
    public void AssemblyReferencedThroughALibraryIsFound()
    {
        // Console.App references Console.UI, which references Ecierge.Uno.Controls.
        Project controls = new("Ecierge.Uno.Controls");
        Project ui = new("Console.UI", controls);
        Project app = new("Console.App", ui);

        ProjectLookupResult<Project>? found = Find(app, app, "Ecierge.Uno.Controls");

        Assert.IsNotNull(found);
        Assert.AreSame(controls, found.Project);
        CollectionAssert.AreEqual(new[] { app, ui, controls }, found.Path.ToArray());
        Assert.AreEqual("'Console.App' references it through 'Console.UI'", found.DescribeRoute(project => project.Name));
    }

    [TestMethod]
    public void ReferenceOfTheOwnerComesBeforeTheProjectsOfTheApplication()
    {
        // The library names its own dependency; a project with the same assembly name nearer to the application loses.
        Project nearer = new("Ecierge.Uno.Controls");
        Project ui = new("Console.UI", new Project("Ecierge.Uno.Controls"));
        Project app = new("Console.App", ui);
        app.References.Add(nearer);

        ProjectLookupResult<Project>? found = Find(ui, app, "Ecierge.Uno.Controls");

        Assert.IsNotNull(found);
        Assert.AreSame(ui.References.Single(), found.Project);
        CollectionAssert.AreEqual(new[] { ui, found.Project }, found.Path.ToArray());
        Assert.AreEqual("'Console.UI' references it", found.DescribeRoute(project => project.Name));
    }

    [TestMethod]
    public void NearestProjectWinsWhenSeveralHaveTheName()
    {
        // Two paths reach a project named Lib: App, A, X, and App, B, C, Lib. The shorter one is the answer.
        Project near = new("Lib");
        Project far = new("Lib");
        Project app = new("App", new Project("A", new Project("X", near)), new Project("B", new Project("C", far)));

        ProjectLookupResult<Project>? found = Find(app, app, "Lib");

        Assert.IsNotNull(found);
        Assert.AreSame(near, found.Project);
        Assert.HasCount(4, found.Path);
    }

    [TestMethod]
    public void CycleOfReferencesEnds()
    {
        Project a = new("A");
        Project b = new("B", a);
        a.References.Add(b);
        Project lib = new("Lib");
        b.References.Add(lib);

        Assert.AreSame(lib, Find(a, a, "Lib")!.Project);
        Assert.IsNull(Find(a, a, "Missing"));
    }

    [TestMethod]
    public void ProjectThatIsReachedByTwoPathsIsLookedAtOnce()
    {
        Project shared = new("Shared");
        Project app = new("App", new Project("A", shared), new Project("B", shared));
        int lookups = 0;

        Assert.IsNull(TransitiveProjectLookup.Find(
            app,
            app,
            project =>
            {
                lookups++;
                return project.References;
            },
            project => project.Name,
            project => project.Id,
            "Missing"));

        // The owner's references, then the application, A, B and Shared once each.
        Assert.AreEqual(5, lookups);
    }

    [TestMethod]
    public void AssemblyNamesAreComparedWithoutCase()
    {
        Project controls = new("Ecierge.Uno.Controls");
        Project app = new("Console.App", new Project("Console.UI", controls));

        Assert.AreSame(controls, Find(app, app, "ecierge.uno.CONTROLS")!.Project);
    }

    [TestMethod]
    public void UnknownAssemblyIsNotFound()
    {
        Project app = new("Console.App", new Project("Console.UI", new Project("Ecierge.Uno.Controls")));

        Assert.IsNull(Find(app, app, "Some.Other.Library"));
    }

    [TestMethod]
    public void WithoutAnApplicationOnlyTheOwnerIsSearched()
    {
        Project controls = new("Ecierge.Uno.Controls");
        Project ui = new("Console.UI", controls);
        Project deeper = new("Console.Deeper", new Project("Console.Inner", new Project("Elsewhere")));

        Assert.AreSame(controls, Find(ui, null, "Ecierge.Uno.Controls")!.Project);
        Assert.IsNull(Find(deeper, null, "Elsewhere"));
    }

    [TestMethod]
    public void ProjectWithoutAListOfReferencesHasNoReferences()
    {
        Project app = new("Console.App");

        Assert.IsNull(TransitiveProjectLookup.Find<Project>(app, app, _ => null, project => project.Name, project => project.Id, "Lib"));
    }

    [TestMethod]
    public void RouteOfAProjectThroughSeveralProjectsNamesThemAll()
    {
        Project lib = new("Lib");
        Project app = new("App", new Project("A", new Project("B", lib)));

        Assert.AreEqual("'App' references it through 'A', 'B'", Find(app, app, "Lib")!.DescribeRoute(project => project.Name));
    }

    private static ProjectLookupResult<Project>? Find(Project owner, Project? application, string assemblyName)
    {
        return TransitiveProjectLookup.Find(owner, application, project => project.References, project => project.Name, project => project.Id, assemblyName);
    }

    private sealed class Project(string name, params Project[] references)
    {
        public string Name { get; } = name;

        public Guid Id { get; } = Guid.NewGuid();

        public List<Project> References { get; } = [.. references];
    }
}
