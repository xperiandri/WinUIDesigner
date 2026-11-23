// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class AttachedPropertyGetterTests
{
    [TestMethod]
    public void AttachedCollectionIsCreatedByItsStaticAccessorOnFirstRead()
    {
        var element = new FakeElement();

        Assert.IsTrue(AttachedPropertyGetter.TryGetValue(typeof(FakeInteraction), "Behaviors", element, out object? first));
        Assert.IsTrue(AttachedPropertyGetter.TryGetValue(typeof(FakeInteraction), "Behaviors", element, out object? second));

        // The accessor stores the collection on the element, so the items that Visual Studio adds stay there.
        Assert.IsInstanceOfType<List<object>>(first);
        Assert.AreSame(first, second);
        Assert.AreSame(first, FakeInteraction.Stored(element));
    }

    [TestMethod]
    public void CollectionWithAnAccessorAndNoPropertyFieldIsRead()
    {
        var element = new FakeElement();

        // VisualStateManager.VisualStateGroups has a static accessor and no DependencyProperty field.
        Assert.IsTrue(AttachedPropertyGetter.TryGetValue(typeof(FakeVisualStateManager), "VisualStateGroups", element, out object? groups));
        Assert.IsInstanceOfType<List<object>>(groups);
        Assert.IsNull(typeof(FakeVisualStateManager).GetField("VisualStateGroupsProperty"));
    }

    [TestMethod]
    public void AccessorMustAcceptTheElement()
    {
        // GetTriggers takes a FakeOtherOwner, which a FakeElement is not.
        Assert.IsNull(AttachedPropertyGetter.Find(typeof(FakeInteraction), "Triggers", typeof(FakeElement)));
        Assert.IsFalse(AttachedPropertyGetter.TryGetValue(typeof(FakeInteraction), "Triggers", new FakeElement(), out object? value));
        Assert.IsNull(value);
    }

    [TestMethod]
    // An instance method, methods with no or two parameters, a method without a result, a non-public method, none.
    [DataRow("InstanceValue")]
    [DataRow("NoParameters")]
    [DataRow("TwoParameters")]
    [DataRow("Nothing")]
    [DataRow("Hidden")]
    [DataRow("Missing")]
    public void OnlyAPublicStaticAccessorWithOneParameterAndAResultCounts(string propertyName)
    {
        Assert.IsNull(AttachedPropertyGetter.Find(typeof(FakeInteraction), propertyName, typeof(FakeElement)));
    }

    [TestMethod]
    [DataRow(typeof(FakeElement), typeof(FakeElement))]
    [DataRow(typeof(FakeDependencyObject), typeof(FakeDependencyObject))]
    public void AccessorForTheMostSpecificOwnerWins(Type targetType, Type expectedParameterType)
    {
        MethodInfo? accessor = AttachedPropertyGetter.Find(typeof(FakeInteraction), "Items", targetType);

        Assert.IsNotNull(accessor);
        Assert.AreEqual(expectedParameterType, accessor.GetParameters()[0].ParameterType);
    }

    [TestMethod]
    public void ExceptionOfTheAccessorIsNotWrapped()
    {
        Assert.Throws<InvalidOperationException>(
            () => AttachedPropertyGetter.TryGetValue(typeof(FakeInteraction), "Broken", new FakeElement(), out _)
        );
    }

    public class FakeDependencyObject
    {
    }

    public sealed class FakeElement : FakeDependencyObject
    {
    }

    public sealed class FakeOtherOwner
    {
    }

    // Shaped like Microsoft.UI.Xaml.VisualStateManager: the attached collection has an accessor and no property field.
    public static class FakeVisualStateManager
    {
        public static List<object> GetVisualStateGroups(FakeDependencyObject owner) => [];
    }

    // Shaped like Microsoft.Xaml.Interactivity.Interaction: the attached collection is created by its accessor.
    public sealed class FakeInteraction
    {
        private static readonly ConditionalWeakTable<FakeDependencyObject, List<object>> _Behaviors = [];

        public static List<object> GetBehaviors(FakeDependencyObject owner) => _Behaviors.GetValue(owner, _ => []);

        public static List<object>? Stored(FakeDependencyObject owner)
        {
            return _Behaviors.TryGetValue(owner, out List<object>? behaviors) ? behaviors : null;
        }

        public static List<object> GetTriggers(FakeOtherOwner owner) => [];

        public static string GetItems(FakeDependencyObject owner) => nameof(FakeDependencyObject);

        public static string GetItems(FakeElement owner) => nameof(FakeElement);

        public static string GetTwoParameters(FakeDependencyObject owner, int index) => string.Empty;

        public static void GetNothing(FakeDependencyObject owner)
        {
        }

        public static object GetBroken(FakeDependencyObject owner) => throw new InvalidOperationException("The accessor failed.");

        internal static object GetHidden(FakeDependencyObject owner) => new();

        public static object GetNoParameters() => new();

        public object GetInstanceValue(FakeDependencyObject owner) => new();
    }
}
