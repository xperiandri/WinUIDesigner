// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using XSurfUwp;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// Reaches into the live collections that Visual Studio's child actions address.
/// </summary>
internal interface IChildCollectionAccess
{
    /// <summary>
    /// Returns the collection whose indices an action on <paramref name="owner"/> refers to: the owner itself when it
    /// is a collection, otherwise the collection it holds, or the owner when it holds none.
    /// </summary>
    object GetCollection(object owner);

    /// <summary>
    /// Returns the number of objects in <paramref name="collection"/>, or <see langword="null"/> when it does not tell.
    /// </summary>
    int? GetCount(object collection);

    /// <summary>
    /// Removes the object at <paramref name="liveIndex"/> of the collection of <paramref name="owner"/>.
    /// </summary>
    /// <returns><see langword="false"/> when there is no object at that index.</returns>
    bool RemoveAt(object owner, int liveIndex);

    /// <summary>
    /// Returns the object at <paramref name="liveIndex"/> of the collection of <paramref name="owner"/>, or <see langword="null"/>.
    /// </summary>
    object? GetAt(object owner, int liveIndex);
}

/// <summary>
/// Applies the child indices of Visual Studio's document model to live collections that lack the elements the
/// namespace policies dropped.
/// </summary>
/// <remarks>
/// <para>
/// Visual Studio addresses the children of a collection by their index in its document model, and that model still
/// contains the dropped elements. Each live collection with dropped elements therefore has a <see cref="ChildIndexMap"/>
/// that knows where they are, and every child action on the collection goes through it: an insertion lands where the
/// document puts it, a removal or a lookup reaches the object the document means.
/// </para>
/// <para>
/// A dropped element reaches the surface in two ways. Construction actions add a <see cref="DroppedElement"/>
/// stand-in, which <see cref="PlaceChild"/> records as a hole instead of adding it. A loaded document registers the
/// positions the XAML rewrite removed elements from through <see cref="Register"/>. A collection without a map keeps
/// the indices exactly as Visual Studio sends them.
/// </para>
/// </remarks>
/// <param name="collections">Reaches into the live collections.</param>
/// <param name="track">Makes a collection with a map part of the document being built or edited, so that it is released with it.</param>
/// <param name="warn">Receives the messages about indices that point nowhere; they change nothing.</param>
internal sealed class ChildIndexTranslator(IChildCollectionAccess collections, Action<object> track, Action<string>? warn = null)
{
    private readonly ChildIndexMaps _maps = new();

    /// <summary>
    /// Gets the maps of the collections with dropped elements.
    /// </summary>
    public ChildIndexMaps Maps => _maps;

    /// <summary>
    /// Decides where a child that Visual Studio adds at <paramref name="modelIndex"/> goes.
    /// </summary>
    /// <param name="owner">The object the action adds to: a collection, or an object that holds one.</param>
    /// <param name="modelIndex">The index of the child in Visual Studio's document model.</param>
    /// <param name="child">The child.</param>
    /// <param name="countPending">
    /// Counts the children of a collection that the current batch adds later, such as setters; they are part of the
    /// collection as Visual Studio sees it. <see langword="null"/> when there are none.
    /// </param>
    /// <returns>
    /// The index in the live collection to insert at; <see langword="null"/> when nothing is added: the child stands
    /// in for a dropped element, which is recorded as a hole, or the owner itself stands in for one.
    /// </returns>
    public int? PlaceChild(object owner, int modelIndex, object? child, Func<object, int>? countPending = null)
    {
        if (owner is DroppedElement)
        {
            // Nothing lives in place of a dropped element, so nothing is added to it.
            return null;
        }

        if (child is not DroppedElement && _maps.Count == 0)
        {
            // Nothing was dropped anywhere: the common case costs no lookup.
            return modelIndex;
        }

        object collection = collections.GetCollection(owner);
        if (child is DroppedElement)
        {
            ChildIndexMap map = _maps.InsertHole(collection, modelIndex, CountLiveChildren(collection, countPending));
            track(collection);
            WinUIDesignerLogger.LogTrace("Surface", $"Child indices: a dropped element takes index {modelIndex} of {Describe(collection)}; dropped elements at {map}.");
            return null;
        }

        if (!_maps.TryGet(collection, out ChildIndexMap? holes))
        {
            return modelIndex;
        }

        int liveIndex = holes.InsertItem(modelIndex, CountLiveChildren(collection, countPending));
        WinUIDesignerLogger.LogTrace("Surface", $"Child indices: AddChild at index {modelIndex} of {Describe(collection)} goes to live index {liveIndex}; dropped elements at {holes}.");
        return liveIndex;
    }

    /// <summary>
    /// Removes the child Visual Studio removes at <paramref name="modelIndex"/>: the object the document model means,
    /// nothing when it means a dropped element, and nothing when the index points past the end. Nothing is removed
    /// from an owner that itself stands in for a dropped element.
    /// </summary>
    /// <param name="owner">The object the action removes from: a collection, or an object that holds one.</param>
    /// <param name="modelIndex">The index of the child in Visual Studio's document model.</param>
    /// <param name="flushPending">
    /// Adds the children of the owner's collection that the current batch has yet to add, such as setters: Visual
    /// Studio's index assumes the earlier actions ran. It runs before the index is translated.
    /// </param>
    public void RemoveChild(object owner, int modelIndex, Action<object>? flushPending = null)
    {
        if (owner is DroppedElement)
        {
            return;
        }

        flushPending?.Invoke(owner);
        object collection = _maps.Count == 0 ? owner : collections.GetCollection(owner);
        int liveIndex = modelIndex;
        if (_maps.Count != 0 && _maps.TryGet(collection, out ChildIndexMap? holes))
        {
            ChildSlot slot = holes.RemoveAt(modelIndex, collections.GetCount(collection) ?? 0);
            switch (slot.Kind)
            {
                case ChildSlotKind.Hole:
                    WinUIDesignerLogger.LogTrace("Surface", $"Child indices: RemoveChild at index {modelIndex} of {Describe(collection)} removes a dropped element; dropped elements at {holes}.");
                    return;
                case ChildSlotKind.Outside:
                    warn?.Invoke($"Child indices: RemoveChild at index {modelIndex} of {Describe(collection)} points past its end; nothing was removed.");
                    return;
                default:
                    liveIndex = slot.LiveIndex;
                    WinUIDesignerLogger.LogTrace("Surface", $"Child indices: RemoveChild at index {modelIndex} of {Describe(collection)} removes live index {liveIndex}; dropped elements at {holes}.");
                    break;
            }
        }

        if (!collections.RemoveAt(owner, liveIndex))
        {
            warn?.Invoke($"Child indices: RemoveChild at index {modelIndex} of {Describe(collection)} points past its end; nothing was removed.");
        }
    }

    /// <summary>
    /// Returns the child Visual Studio looks up at <paramref name="modelIndex"/>: the object the document model means,
    /// a stand-in when it means a dropped element, and <see langword="null"/> when the index points past the end. A
    /// lookup in an owner that itself stands in for a dropped element gives that stand-in again.
    /// </summary>
    /// <param name="owner">The object the action looks into: a collection, or an object that holds one.</param>
    /// <param name="modelIndex">The index of the child in Visual Studio's document model.</param>
    /// <param name="flushPending">
    /// Adds the children of the owner's collection that the current batch has yet to add, such as setters: Visual
    /// Studio's index assumes the earlier actions ran. It runs before the index is translated.
    /// </param>
    public object? GetChild(object owner, int modelIndex, Action<object>? flushPending = null)
    {
        if (owner is DroppedElement)
        {
            // The stand-in ends the path: nothing lives beneath a dropped element.
            return owner;
        }

        flushPending?.Invoke(owner);
        int liveIndex = modelIndex;
        object collection = _maps.Count == 0 ? owner : collections.GetCollection(owner);
        if (_maps.Count != 0 && _maps.TryGet(collection, out ChildIndexMap? holes))
        {
            ChildSlot slot = holes.Find(modelIndex, collections.GetCount(collection) ?? 0);
            switch (slot.Kind)
            {
                case ChildSlotKind.Hole:
                    // Visual Studio builds nothing beneath a dropped element, so the stand-in ends the path.
                    return new DroppedElement();
                case ChildSlotKind.Outside:
                    return null;
                default:
                    liveIndex = slot.LiveIndex;
                    break;
            }
        }

        return collections.GetAt(owner, liveIndex);
    }

    /// <summary>
    /// Gives <paramref name="owner"/>'s collection holes at the model positions <paramref name="holes"/>, replacing the
    /// ones it had. A loaded document registers the elements the XAML rewrite removed this way.
    /// </summary>
    public ChildIndexMap Register(object owner, IEnumerable<int> holes)
    {
        object collection = collections.GetCollection(owner);
        ChildIndexMap map = _maps.Register(collection, holes);
        track(collection);
        return map;
    }

    /// <summary>
    /// Forgets the dropped elements of the collection of <paramref name="owner"/>, for example because it is cleared.
    /// </summary>
    public void Forget(object owner)
    {
        if (_maps.Count != 0)
        {
            _maps.Reset(collections.GetCollection(owner));
        }
    }

    /// <summary>
    /// Forgets the maps of the collections in <paramref name="released"/>, the objects of a released document.
    /// </summary>
    public void Release(IEnumerable<object> released)
    {
        _maps.Release(released);
    }

    /// <summary>
    /// Forgets every map.
    /// </summary>
    public void Clear()
    {
        _maps.Clear();
    }

    private int CountLiveChildren(object collection, Func<object, int>? countPending)
    {
        return (collections.GetCount(collection) ?? 0) + (countPending?.Invoke(collection) ?? 0);
    }

    private static string Describe(object collection)
    {
        return collection.GetType().Name;
    }
}
