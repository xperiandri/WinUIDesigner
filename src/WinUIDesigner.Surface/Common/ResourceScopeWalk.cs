// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Common;

/// <summary>
/// The scopes that a resource reference is looked up in: the owner of the reference and its ancestors, nearest first,
/// then the dictionaries that come after every scope. Generic over the parent function, so that the order is tested
/// without WinUI.
/// </summary>
internal static class ResourceScopeWalk
{
    /// <summary>
    /// Returns <paramref name="owner"/> and its ancestors, nearest first, until an object has no parent or comes back
    /// (a cycle).
    /// </summary>
    public static IEnumerable<T> Ancestors<T>(T owner, Func<T, T?> parentOf)
        where T : class
    {
        var visited = new HashSet<T>(ReferenceEqualityComparer.Instance);
        for (T? current = owner; current is not null && visited.Add(current); current = parentOf(current))
        {
            yield return current;
        }
    }

    /// <summary>
    /// Returns the dictionaries of <paramref name="owner"/> and its ancestors, nearest first, then the dictionaries in
    /// <paramref name="last"/> (the application's, the theme scope's) that are not <see langword="null"/>.
    /// </summary>
    public static IEnumerable<TDictionary> Dictionaries<TScope, TDictionary>(
        TScope owner,
        Func<TScope, TScope?> parentOf,
        Func<TScope, TDictionary?> dictionaryOf,
        IEnumerable<TDictionary?> last)
        where TScope : class
        where TDictionary : class
    {
        foreach (TScope scope in Ancestors(owner, parentOf))
        {
            if (dictionaryOf(scope) is { } dictionary)
            {
                yield return dictionary;
            }
        }

        foreach (TDictionary? dictionary in last)
        {
            if (dictionary is not null)
            {
                yield return dictionary;
            }
        }
    }

    /// <summary>
    /// The parent that a <c>{StaticResource}</c> lookup takes: the search parent that Visual Studio sent with
    /// <c>SetResourcesSearchParentAction</c>, otherwise the live parent.
    /// </summary>
    public static T? SearchParentFirst<T>(T current, IReadOnlyDictionary<T, T> searchParents, Func<T, T?> liveParent)
        where T : class
    {
        return searchParents.TryGetValue(current, out T? parent) ? parent : liveParent(current);
    }

    /// <summary>
    /// The parent that a <c>{ThemeResource}</c> lookup takes: the live parent of an attached object, otherwise the search
    /// parent. Visual Studio never clears a search parent, so it is stale once an edit has moved the object.
    /// </summary>
    public static T? LiveParentFirst<T>(T current, IReadOnlyDictionary<T, T> searchParents, Func<T, T?> liveParent)
        where T : class
    {
        return liveParent(current) ?? (searchParents.TryGetValue(current, out T? parent) ? parent : null);
    }

    /// <summary>
    /// The dictionaries that a <c>{StaticResource}</c> lookup searches, in order: those of <paramref name="owner"/> and its
    /// ancestors, taking the search parent that Visual Studio sent first (<see cref="SearchParentFirst{T}"/>), then
    /// <paramref name="application"/>, then <paramref name="themeScope"/> when there is one.
    /// </summary>
    public static IEnumerable<TDictionary> StaticResourceDictionaries<TScope, TDictionary>(
        TScope owner,
        IReadOnlyDictionary<TScope, TScope> searchParents,
        Func<TScope, TScope?> liveParent,
        Func<TScope, TDictionary?> dictionaryOf,
        TDictionary application,
        TDictionary? themeScope)
        where TScope : class
        where TDictionary : class
    {
        return Dictionaries(
            owner,
            current => SearchParentFirst(current, searchParents, liveParent),
            dictionaryOf,
            [application, themeScope]
        );
    }

    /// <summary>
    /// The dictionaries that a <c>{ThemeResource}</c> lookup searches, in order: those of <paramref name="owner"/> and its
    /// ancestors, taking the live parent of an attached object first (<see cref="LiveParentFirst{T}"/>), then
    /// <paramref name="application"/>, then <paramref name="themeScope"/> when there is one.
    /// </summary>
    public static IEnumerable<TDictionary> ThemeResourceDictionaries<TScope, TDictionary>(
        TScope owner,
        IReadOnlyDictionary<TScope, TScope> searchParents,
        Func<TScope, TScope?> liveParent,
        Func<TScope, TDictionary?> dictionaryOf,
        TDictionary application,
        TDictionary? themeScope)
        where TScope : class
        where TDictionary : class
    {
        return Dictionaries(
            owner,
            current => LiveParentFirst(current, searchParents, liveParent),
            dictionaryOf,
            [application, themeScope]
        );
    }
}
