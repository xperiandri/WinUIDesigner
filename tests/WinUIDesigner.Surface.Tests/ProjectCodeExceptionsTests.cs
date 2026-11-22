// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class ProjectCodeExceptionsTests
{
    // The directory of this assembly stands for the staged files of a project.
    private static readonly string _ProjectDirectory = Path.GetDirectoryName(typeof(ProjectCodeExceptionsTests).Assembly.Location)!;

    private static void ThrowFromProjectCode() => throw new InvalidOperationException("thrown by the code under test");

    private static Exception Capture(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new AssertFailedException("The action was expected to throw.");
    }

    [TestMethod]
    public void ExceptionThrownByCodeInTheProjectDirectoryIsProjectCode()
    {
        Exception thrown = Capture(ThrowFromProjectCode);

        Assert.IsTrue(ProjectCodeExceptions.IsThrownByProjectCode(thrown, _ProjectDirectory));
    }

    [TestMethod]
    public void ExceptionThrownByCodeOutsideTheProjectDirectoryIsNotProjectCode()
    {
        Exception thrown = Capture(ThrowFromProjectCode);
        string anotherProject = Path.Combine(Path.GetTempPath(), "another-project");

        Assert.IsFalse(ProjectCodeExceptions.IsThrownByProjectCode(thrown, anotherProject));
    }

    [TestMethod]
    public void ExceptionThatTheFrameworkThrowsForProjectCodeIsProjectCode()
    {
        AssumeTheFrameworkIsNotInTheProjectDirectory();

        // The caller is project code, the method that throws is in the framework.
        Exception thrown = Capture(() => _ = int.Parse("not a number"));

        Assert.IsTrue(ProjectCodeExceptions.IsThrownByProjectCode(thrown, _ProjectDirectory));
    }

    [TestMethod]
    public void ExceptionThatAThrowHelperThrowsForProjectCodeIsProjectCode()
    {
        AssumeTheFrameworkIsNotInTheProjectDirectory();

        Exception thrown = Capture(() => ArgumentNullException.ThrowIfNull((object?)null, "service"));

        Assert.IsTrue(ProjectCodeExceptions.IsThrownByProjectCode(thrown, _ProjectDirectory));
    }

    [TestMethod]
    public void ExceptionThatLinqThrowsForProjectCodeIsProjectCode()
    {
        AssumeTheFrameworkIsNotInTheProjectDirectory();

        Exception thrown = Capture(() => _ = Array.Empty<int>().First());

        Assert.IsTrue(ProjectCodeExceptions.IsThrownByProjectCode(thrown, _ProjectDirectory));
    }

    [TestMethod]
    public void CodeOfTheSurfaceBetweenTheFrameworkAndTheProjectMakesTheExceptionTheSurfaces()
    {
        AssumeTheFrameworkIsNotInTheProjectDirectory();

        // LINQ stands for the surface here: the method that throws is called by it, and project code calls it.
        Exception thrown = Capture(() => _ = Array.Empty<int>().First());

        Assert.IsFalse(ProjectCodeExceptions.IsThrownByProjectCode(thrown, _ProjectDirectory, surface: typeof(Enumerable).Assembly));
    }

    private static void AssumeTheFrameworkIsNotInTheProjectDirectory()
    {
        if (ProjectCodeExceptions.IsUnder(typeof(int).Assembly.Location, _ProjectDirectory)
            || ProjectCodeExceptions.IsUnder(typeof(Enumerable).Assembly.Location, _ProjectDirectory))
        {
            Assert.Inconclusive("The framework sits next to the test assembly.");
        }
    }

    [TestMethod]
    public void WrapperIsLookedThrough()
    {
        MethodInfo method = typeof(ProjectCodeExceptionsTests).GetMethod(nameof(ThrowFromProjectCode), BindingFlags.NonPublic | BindingFlags.Static)!;
        Exception wrapper = Capture(() => method.Invoke(null, null));

        Assert.IsInstanceOfType<TargetInvocationException>(wrapper);
        Assert.IsTrue(ProjectCodeExceptions.IsThrownByProjectCode(wrapper, _ProjectDirectory));
    }

    [TestMethod]
    public void ExceptionThatWasNeverThrownIsNotProjectCode()
    {
        Assert.IsFalse(ProjectCodeExceptions.IsThrownByProjectCode(new InvalidOperationException(), _ProjectDirectory));
    }

    [TestMethod]
    public void ErrorThatNoManagedMethodThrewIsRaisedByNativeCode()
    {
        // What the framework hands over for a failure of its own: an exception made from an HRESULT, never thrown.
        var error = new System.Runtime.InteropServices.COMException("Unspecified error", unchecked((int)0x80004005));

        Assert.IsTrue(ProjectCodeExceptions.IsRaisedByNativeCode(error));
        Assert.IsFalse(ProjectCodeExceptions.IsThrownByProjectCode(error, _ProjectDirectory));
    }

    [TestMethod]
    public void ExceptionThatManagedCodeThrewIsNotRaisedByNativeCode()
    {
        Assert.IsFalse(ProjectCodeExceptions.IsRaisedByNativeCode(Capture(ThrowFromProjectCode)));
    }

    [TestMethod]
    public void WrapperOfAnErrorThatNoManagedMethodThrewIsRaisedByNativeCode()
    {
        var wrapper = new TargetInvocationException(new System.Runtime.InteropServices.COMException("Unspecified error", unchecked((int)0x80004005)));

        Assert.IsTrue(ProjectCodeExceptions.IsRaisedByNativeCode(wrapper));
    }

    [TestMethod]
    [DataRow(@"C:\project\App.dll", @"C:\project", true)]
    [DataRow(@"C:\project\lib\Controls.dll", @"C:\project", true)]
    [DataRow(@"C:\project\lib\Controls.dll", @"C:\project\", true)]
    [DataRow(@"C:\PROJECT\App.dll", @"C:\project", true)]
    [DataRow(@"C:\project-other\App.dll", @"C:\project", false)]
    [DataRow(@"C:\elsewhere\App.dll", @"C:\project", false)]
    [DataRow("", @"C:\project", false)]
    [DataRow(null, @"C:\project", false)]
    [DataRow(@"C:\project\App.dll", "", false)]
    public void FileIsUnderADirectoryOnlyWhenItIsBelowIt(string? file, string directory, bool expected)
    {
        Assert.AreEqual(expected, ProjectCodeExceptions.IsUnder(file, directory));
    }
}
