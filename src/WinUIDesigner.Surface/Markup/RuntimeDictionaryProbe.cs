// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml.Linq;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Markup;

/// <summary>
/// Asks whether the WinUI runtime loads a resource dictionary by its URI the way the XAML reader is going to load it
/// inside a document: as a dictionary that a dictionary being read merges through <c>Source</c>.
/// </summary>
/// <remarks>
/// A dictionary that is created in code with <c>new ResourceDictionary { Source = uri }</c> answers a slightly
/// different question: it loads on its own, while the reader loads the source in the middle of a parse, with the
/// application's resources and the dictionaries read before it in scope. Only the second answer says whether the
/// document gets what it merges, and a dictionary that loads without providing a single resource gets the document
/// nothing, so the count of resources is part of the answer. The probe is read before the document is, so only the
/// application's resources are in scope while it runs: a dictionary that needs one that the document merges before
/// it fails the probe and is left out, although it would load inside the document. This type holds the two WinUI-free
/// halves of the probe, the markup that is read and the judgement of what the reader returned; the reading itself is
/// done by the surface.
/// </remarks>
internal static class RuntimeDictionaryProbe
{
    /// <summary>
    /// Creates the markup that merges the dictionary <paramref name="uri"/> names into an empty dictionary.
    /// </summary>
    public static string CreateXaml(string uri)
    {
        XNamespace presentation = XamlNamespaces.Presentation;
        var probe = new XElement(
            presentation + "ResourceDictionary",
            new XElement(
                presentation + "ResourceDictionary.MergedDictionaries",
                new XElement(presentation + "ResourceDictionary", new XAttribute("Source", uri))
            )
        );

        return probe.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Judges what the reader returned for the markup of <see cref="CreateXaml"/>.
    /// </summary>
    /// <param name="mergedDictionaries">How many dictionaries the probe's dictionary merges after the read.</param>
    /// <param name="resources">
    /// How many resources the merged dictionaries provide altogether, those they merge in turn and their theme
    /// dictionaries included.
    /// </param>
    /// <returns><see langword="null"/> when the dictionary loads, and the reason otherwise.</returns>
    public static string? Judge(int mergedDictionaries, int resources)
    {
        // The reader skipped it, or merged something else: the source did not yield a dictionary.
        if (mergedDictionaries != 1)
        {
            return $"the XAML reader merged {mergedDictionaries} dictionaries for the source instead of one.";
        }

        // Loaded and empty: nothing the document asks for would be found there, and merging it would only hide that.
        if (resources == 0)
        {
            return "the dictionary loads but provides no resources.";
        }

        return null;
    }
}
