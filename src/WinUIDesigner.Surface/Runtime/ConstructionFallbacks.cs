// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Builds the objects of construction actions, and Visual Studio's fallback in place of an object whose own code
/// fails. Keeps such failures of the current batch of actions for Visual Studio, which shows them on the elements.
/// </summary>
/// <remarks>
/// Visual Studio names a fallback type (<see cref="CreateInstanceAction.FallbackTypeName"/>) for the types of the
/// project, which it cannot vouch for: a <c>FallbackControl</c> for an element, a <c>FallbackValueConverter</c> for a
/// converter. The fallback takes the place of a type that does not resolve, and of one whose construction throws, for
/// example when its <c>InitializeComponent</c> cannot load its XAML.
/// </remarks>
internal sealed class ConstructionFallbacks
{
    private readonly List<ActionError> _failures = [];

    /// <summary>
    /// Runs <paramref name="construct"/>. When it throws and <paramref name="constructFallback"/> is given, runs that
    /// instead and records the exception for <paramref name="action"/>.
    /// </summary>
    /// <param name="action">The action that names the type and its fallback.</param>
    /// <param name="construct">Builds the type that the action names.</param>
    /// <param name="constructFallback">Builds the fallback type, or <see langword="null"/> when there is none.</param>
    /// <returns>
    /// The object, and the exception of the construction, without the wrapper of reflection, when the fallback took its
    /// place. When the fallback throws too, the exception of the construction propagates.
    /// </returns>
    public (T Value, Exception? Failure) Construct<T>(CreateInstanceAction action, Func<T> construct, Func<T>? constructFallback)
    {
        try
        {
            // The type that the action names, which is all that is needed when its code works.
            return (construct(), null);
        }
        catch (Exception ex) when (constructFallback is not null)
        {
            // The construction failed and Visual Studio named a fallback: build that one instead.
            T fallback;
            try
            {
                fallback = constructFallback();
            }
            catch
            {
                // The cause that the author can act on is the failure of the type itself.
                ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }

            Exception failure = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            _failures.Add(new ActionError { XamlAction = action, Error = failure.ToString() });
            return (fallback, failure);
        }
    }

    /// <summary>
    /// Forgets the failures of an earlier batch that ended before they were taken.
    /// </summary>
    public void Clear() => _failures.Clear();

    /// <summary>
    /// Returns the recorded failures, in the order of the actions, and forgets them.
    /// </summary>
    /// <returns>The failures; none when no fallback took the place of an object.</returns>
    public IList<ActionError> TakeFailures()
    {
        ActionError[] failures = [.. _failures];
        _failures.Clear();
        return failures;
    }

    /// <summary>
    /// Returns the recorded failures in the JSON of Visual Studio's <see cref="ActionError"/>, followed by
    /// <paramref name="laterFailure"/>, and forgets them.
    /// </summary>
    /// <param name="laterFailure">
    /// The error of the action that failed after the fallbacks took their places, or <see langword="null"/> when the
    /// actions went on to their end. Visual Studio shows why the fallbacks were needed together with it.
    /// </param>
    /// <returns>The errors, or <see langword="null"/> when there is none.</returns>
    public string? TakeErrors(ActionError? laterFailure = null)
    {
        // The objects that fallbacks took the place of come first, in the order of the actions.
        List<ActionError> errors = [.. TakeFailures()];

        // The action that failed afterwards ended the batch and comes last.
        if (laterFailure is not null)
        {
            errors.Add(laterFailure);
        }

        // Nothing to report when the batch needed no fallback and went through.
        return errors.Count == 0 ? null : ActionErrorJsonSerializer.Serialize(errors);
    }
}
