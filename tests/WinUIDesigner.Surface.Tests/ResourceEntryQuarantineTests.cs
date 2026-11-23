// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Markup;
using WinUIDesigner.Surface.Markup;
using static ShadowCache;

/// <summary>
/// Leaving out the resource of a merged dictionary that the XAML reader rejects. The fixture is the shape of the
/// application that the failure was found on: an App.xaml that merges a package dictionary by URI and the
/// <c>Resources.xaml</c> of three libraries, each of which merges the same shared dictionary, which merges the styles
/// of a control library that the surface cannot load. Some of those styles are based on styles that only that
/// library defines, and the reader rejects the whole text for the first of them. The reader is replaced by a
/// function that does the one thing the quarantine relies on: it rejects the first reference to a key that the text
/// does not define, and reports the position of the attribute the way the XAML reader does.
/// </summary>
[TestClass]
public sealed class ResourceEntryQuarantineTests
{
    private const string _PageUri = "ms-appx:///App/Views/~vs]~10~Page.xaml";
    private const string _ErrorStyles = "ms-appx:///Console.UI/Styles/~vs]~21~ErrorStyles.xaml";
    private const string _UiResources = "ms-appx:///Console.UI/~vs]~22~Resources.xaml";
    private const string _AdminResources = "ms-appx:///Console.Administration/~vs]~23~Resources.xaml";
    private const string _ConstructionResources = "ms-appx:///Console.Construction/~vs]~24~Resources.xaml";
    private const string _Tokenizing = "ms-appx:///CommunityToolkit.WinUI.Controls.TokenizingTextBox/TokenizingTextBox.xaml";

    private static readonly XNamespace _P = XamlNamespaces.Presentation;

    [TestMethod]
    [DataRow("Cannot find a Resource with the Name/Key DefaultLoadingTextBoxStyle [Line: 939 Position: 83]", 939, 83)]
    [DataRow("Failed to assign to property 'Microsoft.UI.Xaml.ResourceDictionary.Source' because the type 'String' cannot be assigned. [Line: 959 Position: 73]", 959, 73)]
    [DataRow("Earlier error [Line: 3 Position: 7]. Cannot locate resource from 'ms-appx:///A/B.xaml'. [Line: 941 Position: 83]", 941, 83)]
    [DataRow("[Line:12   Position:4]", 12, 4)]
    public void PositionIsTheLastOneOfTheMessage(string message, int line, int position)
    {
        Assert.IsTrue(ResourceEntryQuarantine.TryParsePosition(message, out int actualLine, out int actualPosition));
        Assert.AreEqual(line, actualLine);
        Assert.AreEqual(position, actualPosition);
    }

    [TestMethod]
    [DataRow("Cannot locate resource from 'ms-appx:///A/B.xaml'.")]
    [DataRow("[Line: 0 Position: 5]")]
    [DataRow("[Line: 5 Position: 0]")]
    [DataRow("[Line: x Position: 5]")]
    [DataRow("")]
    [DataRow(null)]
    public void MessageWithoutAPositionHasNone(string? message)
    {
        Assert.IsFalse(ResourceEntryQuarantine.TryParsePosition(message, out _, out _));
    }

    [TestMethod]
    [DataRow("{StaticResource DefaultLoadingTextBoxStyle}", "DefaultLoadingTextBoxStyle")]
    [DataRow("{ThemeResource SystemAccentColor}", "SystemAccentColor")]
    [DataRow("{StaticResource ResourceKey=PanelBrush}", "PanelBrush")]
    [DataRow("{  StaticResource   Spaced  }", "Spaced")]
    [DataRow("{Binding Value, Converter={StaticResource BoolToVisibility}}", "BoolToVisibility")]
    public void KeyIsReadFromTheResourceReferenceOfTheAttribute(string value, string expected)
    {
        Assert.IsTrue(ResourceEntryQuarantine.TryGetReferencedKey(value, out string key));
        Assert.AreEqual(expected, key);
    }

    [TestMethod]
    [DataRow("Red")]
    [DataRow("{x:Null}")]
    [DataRow("{Binding Value}")]
    [DataRow("local:LoadingTextBox")]
    [DataRow("")]
    [DataRow(null)]
    public void ValueThatReferencesNoResourceHasNoKey(string? value)
    {
        Assert.IsFalse(ResourceEntryQuarantine.TryGetReferencedKey(value, out _));
    }

    [TestMethod]
    public void RejectedResourceIsLeftOutOfEveryCopyOfItsDictionaryAndTheRestLoads()
    {
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        var quarantine = CreateQuarantine(cache);

        Outcome outcome = LoadUntilItReads(page, quarantine);

        // The reader rejects the first of two resources, then the second, then reads: one read more than resources.
        Assert.HasCount(2, outcome.Removed);
        Assert.AreEqual(3, outcome.Reads);
        Assert.AreEqual(2, quarantine.Count);
        Assert.AreEqual("LoadingTextBoxErrorStyle", outcome.Removed[0].Key);
        Assert.AreEqual("LoadingAutoSuggestBoxErrorStyle", outcome.Removed[1].Key);

        // The three copies of the dictionary in the text lose both, and keep everything else.
        Assert.AreEqual(3, outcome.Removed[0].Copies);
        Assert.AreEqual(3, outcome.Removed[1].Copies);
        Assert.IsEmpty(AllKeysOf(page, "LoadingTextBoxErrorStyle"));
        Assert.IsEmpty(AllKeysOf(page, "LoadingAutoSuggestBoxErrorStyle"));
        foreach (string kept in new[] { "PlainErrorStyle", "TokenizingTextBoxErrorStyle", "LastErrorStyle" })
        {
            Assert.HasCount(3, AllKeysOf(page, kept), kept);
        }

        foreach (string kept in new[] { "UiBrush", "AdminBrush", "ConstructionBrush" })
        {
            Assert.IsNotEmpty(AllKeysOf(page, kept), kept);
        }
    }

    [TestMethod]
    public void ReportNamesTheResourceTheKeyItNeedsAndTheDictionariesMergedByUri()
    {
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");

        QuarantinedResource first = LoadUntilItReads(page, CreateQuarantine(cache)).Removed[0];
        string report = first.Describe("document 10");

        Assert.AreEqual("Console.UI/Styles/ErrorStyles.xaml", first.DictionaryName);
        Assert.AreEqual("Style", first.ElementName);
        Assert.AreEqual("BasedOn", first.Attribute);
        Assert.AreEqual("DefaultLoadingTextBoxStyle", first.NeededKey);
        CollectionAssert.AreEqual(new[] { _Tokenizing }, first.MergedByUri.ToArray());
        Assert.StartsWith("Resource 'LoadingTextBoxErrorStyle' of 'Console.UI/Styles/ErrorStyles.xaml' merged by document 10 is left out of 3 copies of that dictionary: ", report);
        Assert.Contains("the XAML reader rejects it at line ", report);
        Assert.Contains("(BasedOn=\"{StaticResource DefaultLoadingTextBoxStyle}\")", report);
        Assert.Contains("it needs the resource 'DefaultLoadingTextBoxStyle'", report);
        Assert.Contains($"the dictionaries merged by URI before it are {_Tokenizing}", report);
        Assert.Contains("The reader reports: Cannot find a Resource with the Name/Key DefaultLoadingTextBoxStyle [Line: ", report);
    }

    [TestMethod]
    public void DictionaryMergedByUriAfterTheResourceIsNotInScope()
    {
        // The package dictionary comes last: when the style is read it does not exist yet.
        (ShadowCache cache, XElement page) = CreateApplication(packageFirst: false);
        cache.Expander.Expand(page, _PageUri, "document 10");

        QuarantinedResource first = LoadUntilItReads(page, CreateQuarantine(cache), providedByUri: new Dictionary<string, string[]> { [_Tokenizing] = ["DefaultTokenizingTextBoxStyle"] }).Removed[0];

        Assert.IsEmpty(first.MergedByUri);
        Assert.Contains("and no dictionary is merged by URI before it.", first.Describe("document 10"));
    }

    [TestMethod]
    public void ReportedTextIsNotTakenAsTheCause()
    {
        // WinUI describes a rejected BasedOn as a String that cannot be assigned to a Uri, and the same position
        // with different words in other runs. The attribute at the position says what the resource needs.
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        string xaml = page.ToString(SaveOptions.DisableFormatting);
        (_, int line, int position) = FindFirstRejection(xaml);
        string message = $"Failed to assign to property 'Microsoft.UI.Xaml.ResourceDictionary.Source' because the type 'Windows.Foundation.String' cannot be assigned to the type 'Windows.Foundation.Uri'. [Line: {line} Position: {position}]";

        QuarantinedResource? left = CreateQuarantine(cache).TryQuarantine(page, xaml, message);

        Assert.IsNotNull(left);
        Assert.AreEqual("LoadingTextBoxErrorStyle", left.Key);
        Assert.AreEqual("DefaultLoadingTextBoxStyle", left.NeededKey);
        Assert.Contains("The reader reports: Failed to assign to property", left.Describe("document 10"));
    }

    [TestMethod]
    public void QuarantineDoesNotChangeTheDictionariesKeptForLaterDocuments()
    {
        // Whether a key resolves depends on what is merged before the copy. A later document may have it.
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        LoadUntilItReads(page, CreateQuarantine(cache));
        Assert.IsGreaterThan(0, cache.Expander.Cache.Count);

        XElement later = Page($"<ResourceDictionary.MergedDictionaries>{Merge(_ErrorStyles)}</ResourceDictionary.MergedDictionaries>");
        cache.Expander.Expand(later, _PageUri, "document 11");

        CollectionAssert.AreEqual(
            new[] { "PlainErrorStyle", "LoadingTextBoxErrorStyle", "TokenizingTextBoxErrorStyle", "LoadingAutoSuggestBoxErrorStyle", "LastErrorStyle" },
            AllKeysOf(later, null).ToArray());
    }

    [TestMethod]
    public void RejectionInTheDocumentItselfIsNotHandled()
    {
        // The children of the document are counted by Visual Studio for live edits, and the user wrote them.
        var cache = new ShadowCache();
        cache.Add(_ErrorStyles, Dictionary("""<Style x:Key="Shared" TargetType="Button" />"""));
        XElement page = Page($"""<Style x:Key="Own" TargetType="Button" BasedOn="{Reference("MissingStyle")}" />{Merge(_ErrorStyles)}""");
        page.Element(_P + "Border")!.SetAttributeValue("Background", Reference("MissingBrush"));
        cache.Expander.Expand(page, _PageUri, "document 10");

        // The own style is the first rejection of the document and the border the second.
        AssertNothingIsLeftOut("MissingStyle");
        page.Descendants(_P + "Style").First(style => KeyOf(style) == "Own").Remove();
        AssertNothingIsLeftOut("MissingBrush");

        void AssertNothingIsLeftOut(string missing)
        {
            string xaml = page.ToString(SaveOptions.DisableFormatting);
            (string key, int line, int position) = FindFirstRejection(xaml);
            Assert.AreEqual(missing, key);

            Assert.IsNull(CreateQuarantine(cache).TryQuarantine(page, xaml, $"Cannot find a Resource with the Name/Key {key} [Line: {line} Position: {position}]"));
            Assert.AreEqual(xaml, page.ToString(SaveOptions.DisableFormatting));
        }
    }

    [TestMethod]
    public void RejectionWithoutAPositionIsNotHandled()
    {
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        string xaml = page.ToString(SaveOptions.DisableFormatting);

        Assert.IsNull(CreateQuarantine(cache).TryQuarantine(page, xaml, "E_UNEXPECTED"));
        Assert.AreEqual(xaml, page.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void PositionThatStartsNoAttributeIsNotHandled()
    {
        // A position inside an attribute, before it, on an element, or in a dictionary that the runtime loads from a
        // file is no attribute of our text. The position of the reader is not guessed at.
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        string xaml = page.ToString(SaveOptions.DisableFormatting);
        (_, int line, int position) = FindFirstRejection(xaml);

        foreach ((int l, int p) in new[] { (line, position + 1), (line, position - 1), (line, 1), (line + 100000, position), (1, 1) })
        {
            Assert.IsNull(CreateQuarantine(cache).TryQuarantine(page, xaml, $"Cannot find a Resource [Line: {l} Position: {p}]"), $"{l}:{p}");
        }

        Assert.AreEqual(xaml, page.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void TreeThatIsNotTheTextIsNotHandled()
    {
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        string xaml = page.ToString(SaveOptions.DisableFormatting);
        (string key, int line, int position) = FindFirstRejection(xaml);
        page.Element(_P + "Page.Resources")!.AddBeforeSelf(new XElement(_P + "Grid"));

        Assert.IsNull(CreateQuarantine(cache).TryQuarantine(page, xaml, $"Cannot find a Resource with the Name/Key {key} [Line: {line} Position: {position}]"));
    }

    [TestMethod]
    public void RetriesStopAtTheLimit()
    {
        (ShadowCache cache, XElement page) = CreateApplication();
        cache.Expander.Expand(page, _PageUri, "document 10");
        var quarantine = new ResourceEntryQuarantine(cache.Expander.Origins, cache.Expander.IsDictionaryRoot, limit: 1);

        InvalidOperationException rejection = Assert.ThrowsExactly<InvalidOperationException>(() => LoadUntilItReads(page, quarantine));

        Assert.AreEqual(1, quarantine.Count);
        Assert.Contains("LoadingAutoSuggestBoxStyle", rejection.Message);
    }

    [TestMethod]
    public void ResourceOfAThemeDictionaryIsLeftOutAndNotTheDictionary()
    {
        var cache = new ShadowCache();
        cache.Add(_ErrorStyles, Dictionary("""
            <ResourceDictionary.ThemeDictionaries>
              <ResourceDictionary x:Key="Dark">
                <Style x:Key="DarkStyle" TargetType="Button" BasedOn="{StaticResource MissingStyle}" />
                <SolidColorBrush x:Key="DarkBrush" Color="Black" />
              </ResourceDictionary>
            </ResourceDictionary.ThemeDictionaries>
            """));
        XElement page = Page($"<ResourceDictionary.MergedDictionaries>{Merge(_ErrorStyles)}</ResourceDictionary.MergedDictionaries>");
        cache.Expander.Expand(page, _PageUri, "document 10");

        Outcome outcome = LoadUntilItReads(page, CreateQuarantine(cache));

        Assert.HasCount(1, outcome.Removed);
        Assert.AreEqual("DarkStyle", outcome.Removed[0].Key);
        Assert.IsTrue(page.Descendants(_P + "ResourceDictionary").Any(dictionary => KeyOf(dictionary) == "Dark"), "The theme dictionary stays.");
        CollectionAssert.AreEqual(new[] { "DarkBrush" }, AllKeysOf(page, null).ToArray());
    }

    [TestMethod]
    public void ResourceThatHoldsTheRejectedAttributeInATemplateIsLeftOutWhole()
    {
        // The rejected reference is in a dictionary inside the template of a style. The resource other documents
        // refer to is the style, and the style is what goes.
        var cache = new ShadowCache();
        cache.Add(_ErrorStyles, Dictionary("""
            <Style x:Key="TemplatedStyle" TargetType="Button">
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="Button">
                    <Grid>
                      <Grid.Resources>
                        <ResourceDictionary>
                          <SolidColorBrush x:Key="Inner" Color="{StaticResource MissingColor}" />
                        </ResourceDictionary>
                      </Grid.Resources>
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            <Style x:Key="OtherStyle" TargetType="Button" />
            """));
        XElement page = Page($"<ResourceDictionary.MergedDictionaries>{Merge(_ErrorStyles)}</ResourceDictionary.MergedDictionaries>");
        cache.Expander.Expand(page, _PageUri, "document 10");

        Outcome outcome = LoadUntilItReads(page, CreateQuarantine(cache));

        Assert.HasCount(1, outcome.Removed);
        Assert.AreEqual("TemplatedStyle", outcome.Removed[0].Key);
        Assert.AreEqual("MissingColor", outcome.Removed[0].NeededKey);
        CollectionAssert.AreEqual(new[] { "OtherStyle" }, AllKeysOf(page, null).ToArray());
    }

    [TestMethod]
    public void StyleWithoutAKeyIsNamedByItsTargetType()
    {
        var cache = new ShadowCache();
        cache.Add(_ErrorStyles, Dictionary("""<Style TargetType="Button" BasedOn="{StaticResource MissingStyle}" />"""));
        XElement page = Page($"<ResourceDictionary.MergedDictionaries>{Merge(_ErrorStyles)}</ResourceDictionary.MergedDictionaries>");
        cache.Expander.Expand(page, _PageUri, "document 10");

        Outcome outcome = LoadUntilItReads(page, CreateQuarantine(cache));

        Assert.AreEqual("TargetType=Button", outcome.Removed.Single().Key);
        Assert.Contains("Resource 'TargetType=Button' of 'Console.UI/Styles/ErrorStyles.xaml'", outcome.Removed[0].Describe("document 10"));
    }

    [TestMethod]
    public void ExpansionRemembersWhereEachCopyCameFromAlsoWhenItIsServedFromTheCache()
    {
        // Annotations do not survive the duplication of a template; the side table does.
        (ShadowCache cache, XElement first) = CreateApplication();
        XElement second = CreateApplication().Page;
        cache.Expander.Expand(first, _PageUri, "document 10");
        cache.Expander.Expand(second, _PageUri, "document 11");

        foreach (XElement page in new[] { first, second })
        {
            string[] copies = cache.Expander.Origins.FindCopies(page).Select(copy => copy.Value.Name).ToArray();

            CollectionAssert.AreEqual(
                new[]
                {
                    "CommunityToolkit.WinUI.Controls.TokenizingTextBox/TokenizingTextBox.xaml",
                    "Console.UI/Resources.xaml",
                    "Console.UI/Styles/ErrorStyles.xaml",
                    "Console.Administration/Resources.xaml",
                    "Console.UI/Resources.xaml",
                    "Console.UI/Styles/ErrorStyles.xaml",
                    "Console.Construction/Resources.xaml",
                    "Console.UI/Resources.xaml",
                    "Console.UI/Styles/ErrorStyles.xaml",
                },
                copies);
            Assert.AreEqual(1, cache.Expander.Origins.FindCopies(page).Count(copy => copy.Value.IsRuntime));
        }
    }

    [TestMethod]
    public void RetryBudgetAllowsTheFirstRetryAndThenTheTimeOfItsLimit()
    {
        TimeSpan now = TimeSpan.FromHours(5);
        var budget = new RetryBudget(TimeSpan.FromSeconds(10), () => now);

        Assert.IsTrue(budget.TrySpend());
        now += TimeSpan.FromSeconds(10);
        Assert.IsTrue(budget.TrySpend(), "The limit itself is still inside the budget.");
        now += TimeSpan.FromMilliseconds(1);
        Assert.IsFalse(budget.TrySpend());
        Assert.IsFalse(budget.TrySpend());

        budget.Reset();
        Assert.IsTrue(budget.TrySpend());
        now += TimeSpan.FromSeconds(1);
        Assert.IsTrue(budget.TrySpend());
    }

    private static string ErrorStyles()
    {
        return Dictionary($"""
            <Style x:Key="PlainErrorStyle" TargetType="TextBox">
              <Setter Property="BorderBrush" Value="Red" />
            </Style>
            <Style x:Key="LoadingTextBoxErrorStyle"
                   TargetType="local:LoadingTextBox"
                   BasedOn="{Reference("DefaultLoadingTextBoxStyle")}">
              <Setter Property="BorderBrush" Value="Red" />
            </Style>
            <Style x:Key="TokenizingTextBoxErrorStyle" TargetType="local:TokenizingTextBox" BasedOn="{Reference("DefaultTokenizingTextBoxStyle")}" />
            <Style x:Key="LoadingAutoSuggestBoxErrorStyle" TargetType="local:LoadingAutoSuggestBox" BasedOn="{Reference("LoadingAutoSuggestBoxStyle")}" />
            <Style x:Key="LastErrorStyle" TargetType="Button" />
            """);
    }

    private static string Reference(string key)
    {
        return $"{{StaticResource {key}}}";
    }

    /// <summary>
    /// Creates the application: a package dictionary merged by URI, and the resources of three libraries.
    /// </summary>
    private static (ShadowCache Cache, XElement Page) CreateApplication(bool packageFirst = true)
    {
        var cache = new ShadowCache();
        cache.Add(_ErrorStyles, ErrorStyles());
        cache.Add(_UiResources, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_ErrorStyles)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="UiBrush" Color="Red" />"""));
        cache.Add(_AdminResources, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_UiResources)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="AdminBrush" Color="Red" />"""));
        cache.Add(_ConstructionResources, Dictionary($"""<ResourceDictionary.MergedDictionaries>{Merge(_UiResources)}</ResourceDictionary.MergedDictionaries><SolidColorBrush x:Key="ConstructionBrush" Color="Red" />"""));

        string libraries = $"{Merge(_UiResources)}{Merge(_AdminResources)}{Merge(_ConstructionResources)}";
        string merged = packageFirst ? $"{Merge(_Tokenizing)}{libraries}" : $"{libraries}{Merge(_Tokenizing)}";
        return (cache, Page($"<ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries>"));
    }

    private static ResourceEntryQuarantine CreateQuarantine(ShadowCache cache)
    {
        return new ResourceEntryQuarantine(cache.Expander.Origins, cache.Expander.IsDictionaryRoot);
    }

    private static IEnumerable<string?> AllKeysOf(XElement root, string? key)
    {
        // Every keyed resource below a dictionary, of every dictionary that an expansion inserted.
        return root.Descendants()
            .Where(element => element.Parent is { } parent && parent.Name.LocalName == "ResourceDictionary" && !element.Name.LocalName.Contains('.'))
            .Where(element => ResourceDictionarySourceExpander.IsInserted(element) && element.Name.LocalName is not "ResourceDictionary")
            .Select(element => KeyOf(element) ?? $"TargetType={(string?)element.Attribute("TargetType")}")
            .Where(entryKey => key is null || entryKey == key);
    }

    /// <summary>
    /// What the text becomes after the rejected resources are left out one by one, and what happened on the way.
    /// </summary>
    private sealed record Outcome(int Reads, IReadOnlyList<QuarantinedResource> Removed);

    /// <summary>
    /// Reads the text as the surface does: when the reader rejects a resource that the quarantine leaves out, again.
    /// </summary>
    private static Outcome LoadUntilItReads(XElement root, ResourceEntryQuarantine quarantine, Dictionary<string, string[]>? providedByUri = null)
    {
        var removed = new List<QuarantinedResource>();
        for (int reads = 1; ; reads++)
        {
            string xaml = root.ToString(SaveOptions.DisableFormatting);
            try
            {
                Read(xaml, providedByUri);
                return new Outcome(reads, removed);
            }
            catch (InvalidOperationException ex)
            {
                QuarantinedResource? left = quarantine.TryQuarantine(root, xaml, ex.Message);
                if (left is null)
                {
                    throw;
                }

                removed.Add(left);
            }
        }
    }

    /// <summary>
    /// The XAML reader, reduced to the one thing that is tested: a reference to a key that nothing in the text defines
    /// rejects the whole text, at the start of the attribute that holds it.
    /// </summary>
    private static void Read(string xaml, Dictionary<string, string[]>? providedByUri)
    {
        (string key, int line, int position) = FindFirstRejection(xaml, providedByUri);
        if (key.Length != 0)
        {
            throw new InvalidOperationException($"Cannot find a Resource with the Name/Key {key} [Line: {line} Position: {position}]");
        }
    }

    private static (string Key, int Line, int Position) FindFirstRejection(string xaml, Dictionary<string, string[]>? providedByUri = null)
    {
        XDocument document = XDocument.Parse(xaml, LoadOptions.SetLineInfo);
        var defined = new HashSet<string>(document.Descendants().Select(KeyOf).OfType<string>(), StringComparer.Ordinal);
        foreach (XElement runtime in document.Descendants(_P + "ResourceDictionary").Where(element => element.Attribute("Source") is not null))
        {
            defined.UnionWith(providedByUri?[(string)runtime.Attribute("Source")!] ?? ["DefaultTokenizingTextBoxStyle"]);
        }

        foreach (XElement element in document.Root!.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (ResourceEntryQuarantine.TryGetReferencedKey(attribute.Value, out string key) && !defined.Contains(key))
                {
                    var info = (IXmlLineInfo)attribute;
                    return (key, info.LineNumber, info.LinePosition);
                }
            }
        }

        return (string.Empty, 0, 0);
    }
}
