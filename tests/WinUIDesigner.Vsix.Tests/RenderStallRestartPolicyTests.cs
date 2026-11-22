// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Recovery;

[TestClass]
public sealed class RenderStallRestartPolicyTests
{
    private static readonly DateTime _Start = new(2026, 11, 22, 5, 21, 0, DateTimeKind.Utc);
    private static readonly TimeSpan _Window = TimeSpan.FromMinutes(2);

    private static RenderStallRestart? Report(RenderStallProcessReports process, RenderStallRestartPolicy policy, DateTime now, bool contextDisposed = false)
    {
        return process.Decide(policy, RenderStallReport.Message, now, contextDisposed);
    }

    // Every test has a project of its own: the policies of the projects live as long as the process of the tests.
    private static string NewProject() => Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}", "App.csproj");

    [TestMethod]
    public void TheFirstReportOfAProcessRestartsIt()
    {
        var policy = new RenderStallRestartPolicy(3, _Window);

        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), policy, _Start));
    }

    [TestMethod]
    public void AProcessIsRestartedOnceForAllTheDocumentsThatReport()
    {
        var policy = new RenderStallRestartPolicy(2, _Window);
        var process = new RenderStallProcessReports();

        Assert.AreEqual(RenderStallRestart.Restart, Report(process, policy, _Start));
        Assert.IsNull(Report(process, policy, _Start));
        Assert.IsNull(Report(process, policy, _Start.AddSeconds(1)));

        // The reports after the first did not use up the restarts of the project.
        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), policy, _Start.AddSeconds(10)));
    }

    [TestMethod]
    public void OtherUnhandledExceptionsDoNotRestartTheProcess()
    {
        var policy = new RenderStallRestartPolicy(1, _Window);
        var process = new RenderStallProcessReports();

        Assert.IsNull(process.Decide(policy, "The surface process crashed.", _Start, contextDisposed: false));
        Assert.IsNull(process.Decide(policy, null, _Start, contextDisposed: false));
        Assert.AreEqual(RenderStallRestart.Restart, Report(process, policy, _Start));
    }

    [TestMethod]
    public void AProcessThatWasNotRestartedIsRestartedByALaterReportOnceTheLimitAllows()
    {
        var policy = new RenderStallRestartPolicy(1, _Window);
        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), policy, _Start));

        // The restarted process stops drawing again at once: it stays, and the refusal is said once.
        var process = new RenderStallProcessReports();
        Assert.AreEqual(RenderStallRestart.LimitReached, Report(process, policy, _Start.AddSeconds(10)));
        Assert.IsNull(Report(process, policy, _Start.AddSeconds(30)));

        // The designer is reloaded later, and the reloaded document reports the stall from the same process.
        Assert.AreEqual(RenderStallRestart.Restart, Report(process, policy, _Start.Add(_Window)));
    }

    [TestMethod]
    public void NothingIsRestartedOnceTheContextIsDisposed()
    {
        var policy = new RenderStallRestartPolicy(1, _Window);
        var process = new RenderStallProcessReports();

        Assert.IsNull(Report(process, policy, _Start, contextDisposed: true));

        // The report of a disposed context did not use up the restart of the project.
        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), policy, _Start));
    }

    [TestMethod]
    public void TheContextsOfAProjectShareItsRestarts()
    {
        string project = NewProject();
        RenderStallRestartPolicy policy = RenderStallRestartPolicy.ForProject(project, 1, _Window);

        // Visual Studio made a new context for the project when its designers were opened again.
        RenderStallRestartPolicy policyOfTheNextContext = RenderStallRestartPolicy.ForProject(project.ToUpperInvariant(), 1, _Window);

        Assert.AreSame(policy, policyOfTheNextContext);
        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), policy, _Start));
        Assert.AreEqual(RenderStallRestart.LimitReached, Report(new RenderStallProcessReports(), policyOfTheNextContext, _Start.AddSeconds(10)));
    }

    [TestMethod]
    public void EveryProjectHasRestartsOfItsOwn()
    {
        RenderStallRestartPolicy first = RenderStallRestartPolicy.ForProject(NewProject(), 1, _Window);
        RenderStallRestartPolicy second = RenderStallRestartPolicy.ForProject(NewProject(), 1, _Window);

        Assert.AreNotSame(first, second);
        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), first, _Start));
        Assert.AreEqual(RenderStallRestart.Restart, Report(new RenderStallProcessReports(), second, _Start));
    }
}
