// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class MetadataProviderFailuresTests
{
    // What a provider that was compiled against a newer Windows SDK projection than the surface carries throws.
    private const string NewerProjection = "Could not load file or assembly 'Microsoft.Windows.SDK.NET, Version=10.0.29000.0'.";

    // A provider that can be created and then fails whatever it is asked, like a generated provider whose type tables
    // name a type of an assembly that cannot be loaded.
    private sealed class FailingProvider(Exception failure)
    {
        public string? GetXamlType(Type type) => throw failure;
    }

    // A provider that works: it knows one type and answers null for any other, like a generated provider.
    private sealed class KnowingProvider(Type known, string metadata)
    {
        public string? GetXamlType(Type type) => type == known ? metadata : null;
    }

    [TestMethod]
    public void AnswerOfAProviderThatKnowsTheTypeIsReturned()
    {
        var failures = new MetadataProviderFailures((_, _) => Assert.Fail("A provider that answers has not failed."));
        var knowing = new KnowingProvider(typeof(string), "from the provider");

        string? found = XamlTypeLookup.Find<string>(
            typeof(string),
            [failures.Isolate<string>(knowing, knowing.GetXamlType)],
            _ => throw new AssertFailedException("The name must not be asked for when a provider knows the type.")
        );

        Assert.AreEqual("from the provider", found);
    }

    [TestMethod]
    public void ProviderThatDoesNotKnowTheTypeHasNotFailed()
    {
        var warnings = new List<string>();
        var failures = new MetadataProviderFailures((message, _) => warnings.Add(message));
        var knowing = new KnowingProvider(typeof(string), "from the provider");

        Assert.IsNull(failures.Isolate<string>(knowing, knowing.GetXamlType)(typeof(int)));
        Assert.IsEmpty(warnings);
    }

    [TestMethod]
    public void ProviderThatThrowsIsSkipped()
    {
        var failures = new MetadataProviderFailures((_, _) => { });
        var failing = new FailingProvider(new FileLoadException(NewerProjection));

        string? found = XamlTypeLookup.Find<string>(
            typeof(string),
            [failures.Isolate<string>(failing, failing.GetXamlType), _ => "from the second provider"],
            _ => null
        );

        Assert.AreEqual("from the second provider", found);
    }

    [TestMethod]
    public void TypeIsLookedUpByNameWhenTheOnlyProviderThrows()
    {
        var failures = new MetadataProviderFailures((_, _) => { });
        var failing = new FailingProvider(new FileLoadException(NewerProjection));

        string? found = XamlTypeLookup.Find<string>(
            typeof(string),
            [failures.Isolate<string>(failing, failing.GetXamlType)],
            name => $"metadata of {name}"
        );

        Assert.AreEqual("metadata of System.String", found);
    }

    [TestMethod]
    public void FailureIsWrittenToTheLogOnce()
    {
        var warnings = new List<(string Message, Exception Exception)>();
        var failures = new MetadataProviderFailures((message, exception) => warnings.Add((message, exception)));
        var failure = new FileLoadException(NewerProjection);
        var failing = new FailingProvider(failure);

        // XAML asks again and again, about other types too, and every lookup isolates the provider anew.
        Assert.IsNull(failures.Isolate<string>(failing, failing.GetXamlType)(typeof(string)));
        Assert.IsNull(failures.Isolate<string>(failing, failing.GetXamlType)(typeof(int)));
        Assert.IsNull(failures.Isolate<string>(failing, failing.GetXamlType)(typeof(string)));

        (string message, Exception exception) = Assert.ContainsSingle(warnings);
        Assert.AreEqual($"The XAML metadata provider '{typeof(FailingProvider).FullName}' of the project failed to describe the type 'System.String'.", message);
        Assert.AreSame(failure, exception);
    }

    [TestMethod]
    public void DifferentFailuresAreEachWrittenToTheLog()
    {
        var warnings = new List<string>();
        var failures = new MetadataProviderFailures((message, _) => warnings.Add(message));
        var projection = new FailingProvider(new FileLoadException(NewerProjection));
        var runtime = new FailingProvider(new FileLoadException("Could not load file or assembly 'WinRT.Runtime, Version=2.3.0.0'."));

        Assert.IsNull(failures.Isolate<string>(projection, projection.GetXamlType)(typeof(string)));
        Assert.IsNull(failures.Isolate<string>(runtime, runtime.GetXamlType)(typeof(string)));

        Assert.HasCount(2, warnings);
    }

    [TestMethod]
    public void RunningOutOfMemoryIsNotTakenForAFailureOfTheProvider()
    {
        var failures = new MetadataProviderFailures((_, _) => Assert.Fail("Running out of memory is not a failure of the provider."));
        var failing = new FailingProvider(new OutOfMemoryException());

        Assert.Throws<OutOfMemoryException>(() => failures.Isolate<string>(failing, failing.GetXamlType)(typeof(string)));
    }
}
