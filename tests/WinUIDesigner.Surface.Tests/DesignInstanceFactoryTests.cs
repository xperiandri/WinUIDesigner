// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class DesignInstanceFactoryTests
{
    // The assembly-qualified name, as UwpTypeToStringBuilder sends the Type of d:DesignInstance.
    private static readonly string _ViewModelName = typeof(DesignViewModel).AssemblyQualifiedName!;

    private static Type? Resolve(string name) => Type.GetType(name, throwOnError: false);

    [TestMethod]
    public void CreatableTypeIsCreatedFromItsAssemblyQualifiedName()
    {
        object? value = DesignInstanceFactory.Create(_ViewModelName, isDesignTimeCreatable: true, createList: false, Resolve, out Exception? failure);

        Assert.IsInstanceOfType<DesignViewModel>(value);
        Assert.IsNull(failure);
    }

    [TestMethod]
    public void TypeThatTheDesignerMayNotCreateIsNeitherResolvedNorCreated()
    {
        bool resolved = false;

        object? value = DesignInstanceFactory.Create(
            _ViewModelName,
            isDesignTimeCreatable: false,
            createList: false,
            name =>
            {
                resolved = true;
                return Resolve(name);
            },
            out Exception? failure
        );

        Assert.IsNull(value);
        Assert.IsFalse(resolved);
        Assert.IsNull(failure);
    }

    [TestMethod]
    public void CreateListProvidesAListThatHoldsOneInstance()
    {
        object? value = DesignInstanceFactory.Create(_ViewModelName, isDesignTimeCreatable: true, createList: true, Resolve, out Exception? failure);

        List<DesignViewModel> list = Assert.IsInstanceOfType<List<DesignViewModel>>(value);
        Assert.HasCount(1, list);
        Assert.IsNotNull(list[0]);
        Assert.IsNull(failure);
    }

    [TestMethod]
    public void TypeObjectNeedsNoResolution()
    {
        object? value = DesignInstanceFactory.Create(
            typeof(DesignViewModel),
            isDesignTimeCreatable: true,
            createList: false,
            _ => throw new AssertFailedException("A Type needs no resolution."),
            out Exception? failure
        );

        Assert.IsInstanceOfType<DesignViewModel>(value);
        Assert.IsNull(failure);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Missing.ViewModel, Missing.Assembly")]
    public void MissingOrUnknownTypeProvidesNothing(string? typeName)
    {
        object? value = DesignInstanceFactory.Create(typeName, isDesignTimeCreatable: true, createList: true, Resolve, out Exception? failure);

        Assert.IsNull(value);
        Assert.IsNull(failure);
    }

    [TestMethod]
    [DataRow(typeof(ViewModelWithArguments))]
    [DataRow(typeof(ViewModelWithPrivateConstructor))]
    [DataRow(typeof(AbstractViewModel))]
    [DataRow(typeof(IViewModel))]
    [DataRow(typeof(StaticViewModel))]
    [DataRow(typeof(GenericViewModel<>))]
    public void TypeWithoutPublicParameterlessConstructorProvidesNothing(Type type)
    {
        object? value = DesignInstanceFactory.Create(
            type.AssemblyQualifiedName,
            isDesignTimeCreatable: true,
            createList: false,
            Resolve,
            out Exception? failure
        );

        Assert.IsNull(value);
        Assert.IsNull(failure);
    }

    [TestMethod]
    public void ConstructorThatThrowsProvidesNothingAndReportsItsOwnException()
    {
        object? value = DesignInstanceFactory.Create(
            typeof(ThrowingViewModel).AssemblyQualifiedName,
            isDesignTimeCreatable: true,
            createList: true,
            Resolve,
            out Exception? failure
        );

        Assert.IsNull(value);
        // The exception of the constructor itself, not the TargetInvocationException of reflection.
        InvalidOperationException exception = Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.AreEqual(ThrowingViewModel.Message, exception.Message);
    }

    [TestMethod]
    public void ResolutionThatThrowsProvidesNothingAndReportsTheException()
    {
        var thrown = new FileLoadException("The assembly of the view model cannot be loaded.");

        object? value = DesignInstanceFactory.Create(
            _ViewModelName,
            isDesignTimeCreatable: true,
            createList: false,
            _ => throw thrown,
            out Exception? failure
        );

        Assert.IsNull(value);
        Assert.AreSame(thrown, failure);
    }

    public sealed class DesignViewModel
    {
        public string Title { get; set; } = "Design";
    }

    public sealed class ViewModelWithArguments(string title)
    {
        public string Title { get; } = title;
    }

    public sealed class ViewModelWithPrivateConstructor
    {
        private ViewModelWithPrivateConstructor()
        {
        }
    }

    public abstract class AbstractViewModel
    {
    }

    public interface IViewModel
    {
    }

    public static class StaticViewModel
    {
    }

    public sealed class GenericViewModel<T>
    {
    }

    public sealed class ThrowingViewModel
    {
        public const string Message = "The view model needs the application.";

        public ThrowingViewModel() => throw new InvalidOperationException(Message);
    }
}
