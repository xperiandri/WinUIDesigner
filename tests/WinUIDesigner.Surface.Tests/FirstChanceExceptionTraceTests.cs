// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class FirstChanceExceptionTraceTests
{
    [TestMethod]
    public void ExceptionThatIsCaughtLaterIsRecordedWhereItWasThrown()
    {
        using var trace = new FirstChanceExceptionTrace();

        try
        {
            ThrowTheFailure();
        }
        catch (InvalidOperationException)
        {
        }

        string line = Assert.ContainsSingle(trace.Exceptions);
        Assert.StartsWith("System.InvalidOperationException (0x80131509): the first line", line);
        Assert.Contains("ThrowTheFailure", line);
        Assert.DoesNotContain("second line", line);
    }

    [TestMethod]
    public void ExceptionsAreRecordedInTheOrderTheyAreThrown()
    {
        using var trace = new FirstChanceExceptionTrace();

        try { throw new KeyNotFoundException("missing resource"); } catch (KeyNotFoundException) { }
        try { throw new ArgumentException("bad value"); } catch (ArgumentException) { }

        Assert.HasCount(2, trace.Exceptions);
        Assert.Contains("missing resource", trace.Exceptions[0]);
        Assert.Contains("bad value", trace.Exceptions[1]);
    }

    [TestMethod]
    public void ExceptionOfAnotherThreadIsNotRecorded()
    {
        using var trace = new FirstChanceExceptionTrace();

        Task.Run(() =>
        {
            try { throw new InvalidOperationException("other thread"); } catch (InvalidOperationException) { }
        }).Wait();

        Assert.IsEmpty(trace.Exceptions);
    }

    [TestMethod]
    public void OnlyTheFirstExceptionsUpToTheCapacityAreKept()
    {
        using var trace = new FirstChanceExceptionTrace(capacity: 2);

        for (int index = 0; index < 5; index++)
        {
            try { throw new InvalidOperationException($"failure {index}"); } catch (InvalidOperationException) { }
        }

        Assert.HasCount(2, trace.Exceptions);
        Assert.Contains("failure 0", trace.Exceptions[0]);
        Assert.Contains("failure 1", trace.Exceptions[1]);
    }

    [TestMethod]
    public void NothingIsRecordedAfterTheTraceIsDisposed()
    {
        var trace = new FirstChanceExceptionTrace();
        trace.Dispose();

        try { throw new InvalidOperationException("late"); } catch (InvalidOperationException) { }

        Assert.IsEmpty(trace.Exceptions);
    }

    private static void ThrowTheFailure() => throw new InvalidOperationException("the first line\r\nthe second line");
}
