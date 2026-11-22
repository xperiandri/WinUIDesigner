// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;

namespace WinUIDesigner.Surface.Runtime;

/// <summary>
/// Keeps a metadata provider of the project that fails from ending the process. The providers are code of the project,
/// and XAML asks them from inside its own code, where an exception that leaves ends the process (<c>0xC000027B</c>)
/// without a word in the log, for example the <see cref="System.IO.FileLoadException"/> of a provider that was compiled
/// against a newer version of an assembly than the surface carries. It has no WinUI types of its own, so that the rule
/// can be tested.
/// </summary>
internal sealed class MetadataProviderFailures(Action<string, Exception> logWarning)
{
    // The failures already written to the log; XAML asks the same questions again and again.
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    /// <summary>
    /// The question <paramref name="ask"/> about a type to the <paramref name="provider"/>, made safe to ask from inside
    /// XAML: a provider that throws is treated as one that does not know the type, and its failure is written to the log
    /// once.
    /// </summary>
    public Func<Type, T?> Isolate<T>(object provider, Func<Type, T?> ask) where T : class
    {
        return type =>
        {
            try
            {
                return ask(type);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // One entry for each provider and failure, whatever the type: a provider that cannot build its type
                // tables fails for every type that it is asked about.
                string? name = provider.GetType().FullName;
                ReportOnce($"{name}|{ex.GetType().FullName}|{ex.Message}", $"The XAML metadata provider '{name}' of the project failed to describe the type '{type.FullName}'.", ex);

                return null;
            }
        };
    }

    private void ReportOnce(string key, string message, Exception exception)
    {
        lock (_reported)
        {
            if (!_reported.Add(key))
            {
                return;
            }
        }

        logWarning(message, exception);
    }
}
