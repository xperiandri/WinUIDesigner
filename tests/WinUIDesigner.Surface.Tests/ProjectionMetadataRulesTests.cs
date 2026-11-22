// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

/// <summary>
/// Covers how the projection of a Windows Runtime type answers what its metadata would answer. The Windows
/// Runtime is not needed for that: what makes a type part of it is passed in, and the types here are plain.
/// </summary>
[TestClass]
public sealed class ProjectionMetadataRulesTests
{
    private static readonly Func<Type, bool> _NothingIsWindowsRuntime = _ => false;

    [TestMethod]
    [DataRow("Public")]
    [DataRow("Protected")]
    [DataRow("ProtectedInternal")]
    [DataRow("Static")]
    [DataRow("ProtectedSetter")]
    public void PublicAndProtectedPropertiesAreDeclared(string propertyName)
    {
        // Control.DefaultStyleKey and UIElement.ProtectedCursor are protected in the projection and in the metadata.
        Assert.IsTrue(ProjectionMetadataRules.DeclaresProperty(typeof(Declaring), propertyName, _NothingIsWindowsRuntime));
    }

    [TestMethod]
    [DataRow("Private")]
    [DataRow("Internal")]
    [DataRow("PrivateProtected")]
    [DataRow("Changed")]
    [DataRow("NoSuchProperty")]
    public void OtherMembersAreNotDeclaredProperties(string propertyName)
    {
        // The members that the projection keeps for itself are not in the metadata, and an event is no property.
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Declaring), propertyName, _NothingIsWindowsRuntime));
    }

    [TestMethod]
    public void PropertyBelongsToTheTypeThatDeclaresIt()
    {
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Derived), "Public", _NothingIsWindowsRuntime));
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Derived), "Protected", _NothingIsWindowsRuntime));
        Assert.IsTrue(ProjectionMetadataRules.DeclaresProperty(typeof(Derived), "Own", _NothingIsWindowsRuntime));
    }

    [TestMethod]
    public void MembersOfAStructureAreFields()
    {
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Structure), "Computed", _NothingIsWindowsRuntime));
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Structure), "Size", _NothingIsWindowsRuntime));
    }

    [TestMethod]
    [DataRow("Count")]
    [DataRow("IsReadOnly")]
    [DataRow("Item")]
    public void MembersThatTheProjectionAddsForACollectionAreNotDeclared(string propertyName)
    {
        // The metadata of IVector<T> has Size, and no Count, IsReadOnly or indexer.
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Vector), propertyName, _NothingIsWindowsRuntime));
    }

    [TestMethod]
    [DataRow(typeof(Vector))]
    [DataRow(typeof(BindableVector))]
    [DataRow(typeof(Map))]
    [DataRow(typeof(VectorView))]
    public void TypeThatIntroducesACollectionDeclaresSize(Type type)
    {
        Assert.IsTrue(ProjectionMetadataRules.DeclaresProperty(type, "Size", _NothingIsWindowsRuntime));
    }

    [TestMethod]
    public void SizeBelongsToTheTypeThatIntroducesTheCollection()
    {
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(DerivedVector), "Size", _NothingIsWindowsRuntime));
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(Declaring), "Size", _NothingIsWindowsRuntime));
    }

    [TestMethod]
    public void PropertyOfAnInterfaceOfTheWindowsRuntimeKeepsItsName()
    {
        static bool IsWindowsRuntime(Type type) => type == typeof(IRuntimeInterface);

        // The class implements the interface, so the metadata has Value on it; without the mapping it would be a .NET member.
        Assert.IsTrue(ProjectionMetadataRules.DeclaresProperty(typeof(RuntimeClass), "Value", IsWindowsRuntime));
        Assert.IsFalse(ProjectionMetadataRules.DeclaresProperty(typeof(RuntimeClass), "Value", _NothingIsWindowsRuntime));
        Assert.IsTrue(ProjectionMetadataRules.DeclaresProperty(typeof(IRuntimeInterface), "Value", IsWindowsRuntime));
    }

    [TestMethod]
    [DataRow("Microsoft.UI.Xaml.Input.ICommand")]
    [DataRow("Microsoft.UI.Xaml.Data.INotifyPropertyChanged")]
    [DataRow("Microsoft.UI.Xaml.Data.PropertyChangedEventArgs")]
    [DataRow("Microsoft.UI.Xaml.Data.PropertyChangedEventHandler")]
    [DataRow("Microsoft.UI.Xaml.Data.INotifyDataErrorInfo")]
    [DataRow("Microsoft.UI.Xaml.Data.DataErrorsChangedEventArgs")]
    [DataRow("Microsoft.UI.Xaml.Interop.INotifyCollectionChanged")]
    [DataRow("Microsoft.UI.Xaml.Interop.NotifyCollectionChangedAction")]
    [DataRow("Microsoft.UI.Xaml.Interop.NotifyCollectionChangedEventArgs")]
    [DataRow("Microsoft.UI.Xaml.Interop.NotifyCollectionChangedEventHandler")]
    [DataRow("Microsoft.UI.Xaml.CornerRadiusHelper")]
    [DataRow("Microsoft.UI.Xaml.DurationHelper")]
    [DataRow("Microsoft.UI.Xaml.GridLengthHelper")]
    [DataRow("Microsoft.UI.Xaml.ThicknessHelper")]
    [DataRow("Microsoft.UI.Xaml.Controls.Primitives.GeneratorPositionHelper")]
    [DataRow("Microsoft.UI.Xaml.Media.MatrixHelper")]
    [DataRow("Microsoft.UI.Xaml.Media.Animation.KeyTimeHelper")]
    [DataRow("Microsoft.UI.Xaml.Media.Animation.RepeatBehaviorHelper")]
    [DataRow("Microsoft.UI.Xaml.Media.Media3D.Matrix3DHelper")]
    public void TypeThatTheMetadataHasAndTheProjectionLacksIsPresent(string typeName)
    {
        // The reader in an application finds these in Microsoft.UI.Xaml.winmd; C#/WinRT maps or omits them.
        Assert.IsTrue(ProjectionMetadataRules.IsTypeWithoutProjection(typeName));
    }

    [TestMethod]
    [DataRow("Microsoft.UI.Xaml.Controls.Button")]
    [DataRow("Microsoft.UI.Xaml.Interop.IBindableIterable")]
    [DataRow("My.App.CornerRadiusHelper")]
    [DataRow("microsoft.ui.xaml.durationhelper")]
    [DataRow("")]
    public void OtherTypesAreLookedUpInTheProjection(string typeName)
    {
        Assert.IsFalse(ProjectionMetadataRules.IsTypeWithoutProjection(typeName));
    }

    [TestMethod]
    [DataRow("Microsoft.UI.Xaml.DurationHelper", "Automatic", true)]
    [DataRow("Microsoft.UI.Xaml.DurationHelper", "Forever", true)]
    [DataRow("Microsoft.UI.Xaml.GridLengthHelper", "Auto", true)]
    [DataRow("Microsoft.UI.Xaml.Media.MatrixHelper", "Identity", true)]
    [DataRow("Microsoft.UI.Xaml.Media.Media3D.Matrix3DHelper", "Identity", true)]
    [DataRow("Microsoft.UI.Xaml.Media.Animation.RepeatBehaviorHelper", "Forever", true)]
    [DataRow("Microsoft.UI.Xaml.Data.PropertyChangedEventArgs", "PropertyName", true)]
    [DataRow("Microsoft.UI.Xaml.Data.INotifyDataErrorInfo", "HasErrors", true)]
    [DataRow("Microsoft.UI.Xaml.Interop.NotifyCollectionChangedEventArgs", "NewItems", true)]
    [DataRow("Microsoft.UI.Xaml.Interop.NotifyCollectionChangedEventArgs", "OldStartingIndex", true)]
    [DataRow("Microsoft.UI.Xaml.DurationHelper", "NoSuchProperty", false)]
    [DataRow("Microsoft.UI.Xaml.Input.ICommand", "CanExecute", false)]
    [DataRow("Microsoft.UI.Xaml.Controls.Button", "Background", false)]
    public void PropertiesOfTheseTypesAreThoseOfTheMetadata(string typeName, string propertyName, bool expected)
    {
        Assert.AreEqual(expected, ProjectionMetadataRules.DeclaresPropertyWithoutProjection(typeName, propertyName));
    }

    private class Declaring
    {
        public event EventHandler? Changed;

        public static int Static { get; set; }

        public int Public { get; set; }

        public int ProtectedSetter { get; protected set; }

        protected int Protected { get; set; }

        protected internal int ProtectedInternal { get; set; }

        private protected int PrivateProtected { get; set; }

        internal int Internal { get; set; }

        private int Private { get; set; }

        protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Derived : Declaring
    {
        public int Own { get; set; }
    }

    private struct Structure
    {
        public double Size { get; set; }

        public double Computed => Size * 2;
    }

    // What C#/WinRT makes of a class that implements IVector<T>: the members of the .NET interfaces.
    private class Vector : IList<string>
    {
        public int Count => 0;

        public bool IsReadOnly => true;

        public string this[int index]
        {
            get => string.Empty;
            set { }
        }

        public void Add(string item)
        {
        }

        public void Clear()
        {
        }

        public bool Contains(string item) => false;

        public void CopyTo(string[] array, int arrayIndex)
        {
        }

        public IEnumerator<string> GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();

        public int IndexOf(string item) => -1;

        public void Insert(int index, string item)
        {
        }

        public bool Remove(string item) => false;

        public void RemoveAt(int index)
        {
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class DerivedVector : Vector;

    // IBindableVector, IMapView and IVectorView are projected as the non-generic list, the dictionary and the read-only list.
    private sealed class BindableVector : IList
    {
        public int Count => 0;

        public bool IsReadOnly => true;

        public bool IsFixedSize => false;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public object? this[int index]
        {
            get => null;
            set { }
        }

        public int Add(object? value) => 0;

        public void Clear()
        {
        }

        public bool Contains(object? value) => false;

        public void CopyTo(Array array, int index)
        {
        }

        public IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();

        public int IndexOf(object? value) => -1;

        public void Insert(int index, object? value)
        {
        }

        public void Remove(object? value)
        {
        }

        public void RemoveAt(int index)
        {
        }
    }

    private sealed class Map : IDictionary<string, int>
    {
        public int this[string key]
        {
            get => 0;
            set { }
        }

        public ICollection<string> Keys => [];

        public ICollection<int> Values => [];

        public int Count => 0;

        public bool IsReadOnly => true;

        public void Add(string key, int value)
        {
        }

        public void Add(KeyValuePair<string, int> item)
        {
        }

        public void Clear()
        {
        }

        public bool Contains(KeyValuePair<string, int> item) => false;

        public bool ContainsKey(string key) => false;

        public void CopyTo(KeyValuePair<string, int>[] array, int arrayIndex)
        {
        }

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, int>>().GetEnumerator();

        public bool Remove(string key) => false;

        public bool Remove(KeyValuePair<string, int> item) => false;

        public bool TryGetValue(string key, out int value)
        {
            value = 0;
            return false;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class VectorView : IReadOnlyList<string>
    {
        public int Count => 0;

        public string this[int index] => string.Empty;

        public IEnumerator<string> GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private interface IRuntimeInterface
    {
        int Value { get; }
    }

    private sealed class RuntimeClass : IRuntimeInterface
    {
        public int Value => 1;
    }
}
