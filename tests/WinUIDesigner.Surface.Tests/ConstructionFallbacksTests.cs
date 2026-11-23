// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Common;
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

    // Another control of the project, with the same fallback.
    private static CreateInstanceAction CreateSideBar()
    {
        return new(-41, "Ecierge.Console.Components.SideBar, Ecierge.Console.UI")
        {
            FallbackTypeName = "XSurfUwp.Fallback.FallbackControl, UwpSurface",
        };
    }

    // The error of an action that fails after the fallbacks took their places.
    private static ActionError CreateLaterFailure() => new() { XamlAction = new SetSurfaceContentAction(42, 123), Error = "later failure" };

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
    public void FailuresAreTakenInTheOrderOfTheActionsAndForgotten()
    {
        var fallbacks = new ConstructionFallbacks();
        fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException("title bar failure"), () => new object());
        fallbacks.Construct<object?>(CreateSideBar(), () => throw new InvalidOperationException("side bar failure"), () => new object());

        IList<ActionError> failures = fallbacks.TakeFailures();

        Assert.HasCount(2, failures);
        Assert.AreEqual(-39, Assert.IsInstanceOfType<CreateInstanceAction>(failures[0].XamlAction).LiveHandle);
        Assert.Contains("title bar failure", failures[0].Error);
        Assert.AreEqual(-41, Assert.IsInstanceOfType<CreateInstanceAction>(failures[1].XamlAction).LiveHandle);
        Assert.Contains("side bar failure", failures[1].Error);
        Assert.IsEmpty(fallbacks.TakeFailures());
    }

    [TestMethod]
    public void FailureOfALaterActionOfAnEditIsReportedAfterTheReplacedElement()
    {
        var fallbacks = new ConstructionFallbacks();
        fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException("constructor failure"), () => new object());

        // A property of the original type fails on the fallback that replaced it, and stops the batch.
        string? errors = fallbacks.TakeErrors(CreateLaterFailure());

        Assert.IsNotNull(errors);
        IList<ActionError> decoded = ActionErrorJsonSerializer.Deserialize(errors);
        Assert.HasCount(2, decoded);
        // The reason for the replacement first, so that Visual Studio shows it on the element, then the later failure.
        Assert.IsInstanceOfType<CreateInstanceAction>(decoded[0].XamlAction);
        Assert.Contains("constructor failure", decoded[0].Error);
        Assert.IsInstanceOfType<SetSurfaceContentAction>(decoded[1].XamlAction);
        Assert.AreEqual("later failure", decoded[1].Error);
        Assert.IsNull(fallbacks.TakeErrors());
    }

    [TestMethod]
    public void FailureOfALaterActionOfAnEditIsReportedAloneWhenNoFallbackWasNeeded()
    {
        var fallbacks = new ConstructionFallbacks();

        string? errors = fallbacks.TakeErrors(CreateLaterFailure());

        Assert.IsNotNull(errors);
        IList<ActionError> decoded = ActionErrorJsonSerializer.Deserialize(errors);
        Assert.HasCount(1, decoded);
        Assert.AreEqual("later failure", decoded[0].Error);
    }

    [TestMethod]
    public void FailureOfADocumentCarriesTheReplacedElementsBeforeTheFailedAction()
    {
        var fallbacks = new ConstructionFallbacks();
        fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException("constructor failure"), () => new object());

        var failure = new DocumentConstructionException(
            new InvalidOperationException("later failure"),
            new SetSurfaceContentAction(42, 123),
            fallbacks.TakeFailures()
        );

        // The document is not built, so this message is all that Visual Studio learns about the replaced element.
        IList<ActionError> decoded = ActionErrorJsonSerializer.Deserialize(failure.SerializedErrors);
        Assert.HasCount(2, decoded);
        Assert.IsInstanceOfType<CreateInstanceAction>(decoded[0].XamlAction);
        Assert.Contains("constructor failure", decoded[0].Error);
        Assert.IsInstanceOfType<SetSurfaceContentAction>(decoded[1].XamlAction);
        Assert.Contains("later failure", decoded[1].Error);
        Assert.IsEmpty(fallbacks.TakeFailures());
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
    public void ActionThatSetsOrClearsAPropertyOrAddsAChildCanBeLeftOut()
    {
        Assert.IsTrue(ConstructionFallbacks.CanGoOnWithout(new SetPropertyAction(-3, "Foreground:Microsoft.UI.Xaml.Controls.Control", -5, 17064790)));
        Assert.IsTrue(ConstructionFallbacks.CanGoOnWithout(new ClearPropertyAction(-3, "Foreground:Microsoft.UI.Xaml.Controls.Control", 17064790)));
        Assert.IsTrue(ConstructionFallbacks.CanGoOnWithout(new AddChildAction(-3, 0, -5)));
    }

    [TestMethod]
    public void ActionThatBuildsAnObjectOrNamesTheContentCannotBeLeftOut()
    {
        // The actions after these work on the object that they were to make, or on the content that they were to name.
        Assert.IsFalse(ConstructionFallbacks.CanGoOnWithout(CreateTitleBar()));
        Assert.IsFalse(ConstructionFallbacks.CanGoOnWithout(new SetSurfaceContentAction(42, 123)));
        Assert.IsFalse(ConstructionFallbacks.CanGoOnWithout(new GetPropertyAction(-3, "Children:Microsoft.UI.Xaml.Controls.Panel", -5, 17064790)));
    }

    [TestMethod]
    public void ActionThatWasLeftOutIsReportedForItsElementWithoutTheWrapperOfReflection()
    {
        var fallbacks = new ConstructionFallbacks();
        var action = new SetPropertyAction(-219, "Foreground:Microsoft.UI.Xaml.Controls.Control", -225, 17064790);

        fallbacks.RecordFailure(action, new System.Reflection.TargetInvocationException(new InvalidOperationException("The framework refused the value.")));

        string? errors = fallbacks.TakeErrors();
        Assert.IsNotNull(errors);
        IList<ActionError> decoded = ActionErrorJsonSerializer.Deserialize(errors);
        Assert.HasCount(1, decoded);
        SetPropertyAction reported = Assert.IsInstanceOfType<SetPropertyAction>(decoded[0].XamlAction);
        Assert.AreEqual(-219, reported.ParentHandle);
        Assert.Contains("The framework refused the value.", decoded[0].Error);
        Assert.DoesNotContain(nameof(System.Reflection.TargetInvocationException), decoded[0].Error);
    }

    [TestMethod]
    public void ActionsThatWereLeftOutAreReportedInTheirOrderAfterTheReplacedElements()
    {
        var fallbacks = new ConstructionFallbacks();
        fallbacks.Construct<object?>(CreateTitleBar(), () => throw new InvalidOperationException("constructor failure"), () => new object());
        fallbacks.RecordFailure(new AddChildAction(-3, 0, -5), new InvalidOperationException("no collection"));
        fallbacks.RecordFailure(new SetPropertyAction(-3, "Style:Microsoft.UI.Xaml.FrameworkElement", -7, 17118908), new KeyNotFoundException("missing style"));

        IList<ActionError> failures = fallbacks.TakeFailures();

        Assert.HasCount(3, failures);
        Assert.IsInstanceOfType<CreateInstanceAction>(failures[0].XamlAction);
        Assert.IsInstanceOfType<AddChildAction>(failures[1].XamlAction);
        Assert.Contains("no collection", failures[1].Error);
        Assert.IsInstanceOfType<SetPropertyAction>(failures[2].XamlAction);
        Assert.Contains("missing style", failures[2].Error);
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
