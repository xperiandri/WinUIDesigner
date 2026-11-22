// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class ConstructionFallbacksTests
{
    // A control of the project whose InitializeComponent fails, with the fallback that Visual Studio names for it.
    private static CreateInstanceAction CreateTitleBar()
    {
        return new(-39, "Ecierge.Console.Components.TitleBar, Ecierge.Console.UI")
        {
            FallbackTypeName = "XSurfUwp.Fallback.FallbackControl, UwpSurface",
        };
    }

    [TestMethod]
    public void ConstructionThatSucceedsKeepsItsObjectAndReportsNothing()
    {
        var fallbacks = new ConstructionFallbacks();
        var created = new object();

        (object? value, Exception? failure) = fallbacks.Construct<object?>(
            CreateTitleBar(),
            () => created,
            () => throw new AssertFailedException("The fallback is not needed.")
        );

        Assert.AreSame(created, value);
        Assert.IsNull(failure);
        Assert.IsNull(fallbacks.TakeErrors());
    }

    [TestMethod]
    public void FallbackTakesThePlaceOfAConstructionThatThrows()
    {
        var fallbacks = new ConstructionFallbacks();
        var fallback = new object();
        var thrown = new InvalidOperationException("'Behaviors' was not found in type 'Microsoft.Xaml.Interactivity.Interaction'.");

        (object? value, Exception? failure) = fallbacks.Construct<object?>(CreateTitleBar(), () => throw thrown, () => fallback);

        Assert.AreSame(fallback, value);
        Assert.AreSame(thrown, failure);
    }

    [TestMethod]
    public void FailureIsReportedForTheElementThatTheFallbackReplaced()
    {
        var fallbacks = new ConstructionFallbacks();
        fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException("constructor failure"), () => new object());

        string? errors = fallbacks.TakeErrors();

        // Visual Studio finds the element through the live handle of the action and shows the error on it.
        Assert.IsNotNull(errors);
        IList<ActionError> decoded = ActionErrorJsonSerializer.Deserialize(errors);
        Assert.HasCount(1, decoded);
        CreateInstanceAction action = Assert.IsInstanceOfType<CreateInstanceAction>(decoded[0].XamlAction);
        Assert.AreEqual(-39, action.LiveHandle);
        Assert.AreEqual("Ecierge.Console.Components.TitleBar, Ecierge.Console.UI", action.TypeName);
        Assert.Contains("constructor failure", decoded[0].Error);

        // The errors are taken once.
        Assert.IsNull(fallbacks.TakeErrors());
    }

    [TestMethod]
    public void ConstructorExceptionIsReportedWithoutTheWrapperOfReflection()
    {
        var fallbacks = new ConstructionFallbacks();

        (_, Exception? failure) = fallbacks.Construct<object?>(
            CreateTitleBar(),
            () => Activator.CreateInstance(typeof(ThrowingControl)),
            () => new object()
        );

        InvalidOperationException exception = Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual(ThrowingControl.Message, exception.Message);
        string? errors = fallbacks.TakeErrors();
        Assert.IsNotNull(errors);
        Assert.Contains(ThrowingControl.Message, errors);
        Assert.DoesNotContain(nameof(System.Reflection.TargetInvocationException), errors);
    }

    [TestMethod]
    public void WithoutFallbackTheExceptionPropagatesAndNothingIsReported()
    {
        var fallbacks = new ConstructionFallbacks();

        Assert.Throws<InvalidOperationException>(
            () => fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException(), null)
        );
        Assert.IsNull(fallbacks.TakeErrors());
    }

    [TestMethod]
    public void FallbackThatThrowsTooLetsTheExceptionOfTheConstructionPropagate()
    {
        var fallbacks = new ConstructionFallbacks();
        var thrown = new InvalidOperationException("constructor failure");

        InvalidOperationException propagated = Assert.Throws<InvalidOperationException>(
            () => fallbacks.Construct<object?>(CreateTitleBar(), () => throw thrown, () => throw new NotSupportedException())
        );

        Assert.AreSame(thrown, propagated);
        Assert.IsNull(fallbacks.TakeErrors());
    }

    [TestMethod]
    public void ClearForgetsTheFailuresOfAnEarlierBatch()
    {
        var fallbacks = new ConstructionFallbacks();
        fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException(), () => new object());

        fallbacks.Clear();

        Assert.IsNull(fallbacks.TakeErrors());
    }

    public sealed class ThrowingControl
    {
        public const string Message = "InitializeComponent failed.";

        public ThrowingControl() => throw new InvalidOperationException(Message);
    }
}
