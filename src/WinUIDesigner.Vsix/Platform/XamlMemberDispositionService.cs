// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.Utility.Text;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Platform;

/// <summary>
/// Answers what the namespace policies decide for a document node or one of its properties,
/// judged by the prefix it was written with.
/// </summary>
/// <remarks>
/// <para>
/// Construction actions carry CLR names only, so on that path the policies have to be applied here, on
/// the document model. A document without policy namespaces is recognized from one scan of its text per
/// text version and never has the source of its nodes read. Instances are safe to use from any thread.
/// </para>
/// <para>
/// A document is judged with one set of answers to its conditions for as long as its verdicts are kept: until
/// its text changes, the policy inputs change (<see cref="TargetPlatform"/>, <see cref="EvaluateCondition"/>),
/// or <see cref="Refresh"/> is called for it. A question has one answer within that time, whenever it is
/// asked and whichever node asks, so the two branches of an if/else pair always agree. New answers
/// (<see cref="AnswersChanged"/>) do not replace the verdicts of a document that may be being built, on
/// another thread, in the middle of its construction.
/// </para>
/// </remarks>
internal sealed class XamlMemberDispositionService
{
    private readonly ConditionalWeakTable<IDocumentRoot, DocumentCache> _documents = new();
    private volatile string _targetPlatform = UnoPlatformPrefixes.DefaultTargetPlatform;
    private volatile XamlConditionEvaluator? _evaluateCondition;
    private int _generation;
    private int _answersVersion;

    /// <summary>
    /// Gets or sets the platform whose members are kept. Changing it discards the cached verdicts.
    /// </summary>
    public string TargetPlatform
    {
        get => _targetPlatform;
        set
        {
            string platform = string.IsNullOrEmpty(value) ? UnoPlatformPrefixes.DefaultTargetPlatform : value;
            if (!string.Equals(_targetPlatform, platform, StringComparison.Ordinal))
            {
                _targetPlatform = platform;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Gets or sets the evaluator for conditional namespaces. Without one, their members are left untouched.
    /// Changing it discards the cached verdicts.
    /// </summary>
    public XamlConditionEvaluator? EvaluateCondition
    {
        get => _evaluateCondition;
        set
        {
            if (!ReferenceEquals(_evaluateCondition, value))
            {
                _evaluateCondition = value;
                Invalidate();
            }
        }
    }

    /// <summary>
    /// Discards the cached verdicts of every document: the next question about a document looks at its text again.
    /// </summary>
    public void Invalidate() => Interlocked.Increment(ref _generation);

    /// <summary>
    /// Notes that the answers to conditions changed. A document keeps the verdicts it has until
    /// <see cref="Refresh"/> is called for it or its text changes.
    /// </summary>
    public void AnswersChanged() => Interlocked.Increment(ref _answersVersion);

    /// <summary>
    /// Makes the next question about <paramref name="document"/> look at its text again when answers changed
    /// since it was last looked at. Call it for a document that is about to be built, not for one that is being built.
    /// </summary>
    public void Refresh(IDocumentRoot? document)
    {
        // A document that was never asked about has nothing to discard.
        if (document is null || !_documents.TryGetValue(document, out DocumentCache? cache))
        {
            return;
        }

        // A state that was made with the answers of now stays.
        DocumentState? state = Volatile.Read(ref cache.State);
        if (state is not null && state.AnswersVersion != Volatile.Read(ref _answersVersion))
        {
            Interlocked.CompareExchange(ref cache.State, null, state);
        }
    }

    /// <summary>
    /// Returns the namespace facts of the current text of <paramref name="document"/>. They are scanned once
    /// per text version; a document without text yields <see cref="XamlNamespaceScanResult.Empty"/>.
    /// </summary>
    public XamlNamespaceScanResult GetNamespaces(IDocumentRoot? document)
    {
        try
        {
            return GetState(document)?.Namespaces ?? XamlNamespaceScanResult.Empty;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return XamlNamespaceScanResult.Empty;
        }
    }

    /// <summary>
    /// Returns the verdict for the element <paramref name="node"/> was written as.
    /// Nodes without source, and failures of any kind, yield <see cref="XamlPrefixDisposition.Leave"/>.
    /// </summary>
    public XamlPrefixDisposition GetNodeDisposition(DocumentNode? node)
    {
        // There is no node to judge.
        if (node is null)
        {
            return XamlPrefixDisposition.Leave;
        }

        try
        {
            DocumentState? state = GetState(node.DocumentRoot);
            ConcurrentDictionary<object, XamlPrefixDisposition>? verdicts = state?.Verdicts;
            object? site = verdicts is null ? null : node.SourceContext;

            // A document without policy namespaces, or a node without source, is left alone.
            if (state is null || verdicts is null || site is null)
            {
                return XamlPrefixDisposition.Leave;
            }

            // The source of a place is read once; the verdict is kept for the next question about it.
            if (!verdicts.TryGetValue(site, out XamlPrefixDisposition verdict))
            {
                verdict = Classify(XamlSourcePrefixReader.ReadElementPrefix(node), state);
                verdicts[site] = verdict;
            }

            return verdict;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return XamlPrefixDisposition.Leave;
        }
    }

    /// <summary>
    /// Returns the verdict for <paramref name="property"/> as it was written on <paramref name="parentNode"/>,
    /// as an attribute or as a property element. Properties without source, and failures of any kind,
    /// yield <see cref="XamlPrefixDisposition.Leave"/>.
    /// </summary>
    public XamlPrefixDisposition GetPropertyDisposition(DocumentCompositeNode? parentNode, IProperty? property)
    {
        // There is no property to judge.
        if (parentNode is null || property is null)
        {
            return XamlPrefixDisposition.Leave;
        }

        try
        {
            // The source reference of the attribute or property element identifies this place in the text.
            DocumentState? state = GetState(parentNode.DocumentRoot);
            ConcurrentDictionary<object, XamlPrefixDisposition>? verdicts = state?.Verdicts;
            object? site = verdicts is null ? null : parentNode.GetContainerContext(property);

            // A document without policy namespaces, or a property without source, is left alone.
            if (state is null || verdicts is null || site is null)
            {
                return XamlPrefixDisposition.Leave;
            }

            // The source of a place is read once; the verdict is kept for the next question about it.
            if (!verdicts.TryGetValue(site, out XamlPrefixDisposition verdict))
            {
                verdict = Classify(XamlSourcePrefixReader.ReadPropertyPrefix(parentNode, property), state);
                verdicts[site] = verdict;
            }

            return verdict;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return XamlPrefixDisposition.Leave;
        }
    }

    /// <summary>
    /// Returns whether the policies may drop members of <paramref name="document"/>: it declares a namespace that
    /// one of them applies to.
    /// </summary>
    public bool HasPolicyNamespaces(IDocumentRoot? document)
    {
        try
        {
            return GetState(document)?.Verdicts is not null;
        }
        catch (Exception ex)
        {
            LogFailure(ex);
            return false;
        }
    }

    /// <summary>
    /// Returns, for every element of <paramref name="document"/> except its root and for every property that an
    /// element has, whether the policies drop it.
    /// </summary>
    public Dictionary<XamlVerdictPlace, bool> GetDroppedPlaces(IDocumentRoot? document)
    {
        var places = new Dictionary<XamlVerdictPlace, bool>();
        if (document?.RootNode is not { } root)
        {
            return places;
        }

        AddPlaces(root, places);
        foreach (DocumentNode node in root.DescendantNodes)
        {
            AddPlaces(node, places);
        }

        return places;
    }

    private void AddPlaces(DocumentNode node, Dictionary<XamlVerdictPlace, bool> places)
    {
        // A value is no element, and has no properties.
        if (node is not DocumentCompositeNode element)
        {
            return;
        }

        // The root is exempt: a document needs its root.
        if (element.Parent is not null)
        {
            places[new XamlVerdictPlace(element, null)] = GetNodeDisposition(element) == XamlPrefixDisposition.Drop;
        }

        foreach (KeyValuePair<IProperty, DocumentNode> property in element.Properties)
        {
            places[new XamlVerdictPlace(element, property.Key)] = GetPropertyDisposition(element, property.Key) == XamlPrefixDisposition.Drop;
        }
    }

    private static void LogFailure(Exception exception)
    {
        // This runs for every property that is built: a failure costs a skipped policy and, unless
        // verbose logging is on, nothing else.
        WinUIDesignerLogger.LogTrace("Platform", $"Reading the namespace policy of a document node failed: {exception}");
    }

    /// <summary>
    /// Returns the cached state of the current text of a document, or <see langword="null"/> when the
    /// document has no text.
    /// </summary>
    private DocumentState? GetState(IDocumentRoot? root)
    {
        // A document without a text buffer has no namespaces to scan.
        IMarkupTextBuffer? buffer = root?.TextBuffer;
        if (root is null || buffer is null)
        {
            return null;
        }

        DocumentCache cache = _documents.GetValue(root, static _ => new DocumentCache());
        int textVersion = buffer.Version;
        int textLength = buffer.Length;
        int generation = Volatile.Read(ref _generation);

        // New answers alone do not replace a state: the document may be being built with it right now.
        DocumentState? state = Volatile.Read(ref cache.State);
        if (state is null || state.TextVersion != textVersion || state.TextLength != textLength || state.Generation != generation)
        {
            // One pass over the text each time it changes: the namespaces of a document are declared in its
            // text. Not every buffer counts versions (SimpleTextBuffer always reports 0), so the length
            // takes part in telling one text from the next.
            int answersVersion = Volatile.Read(ref _answersVersion);
            var outcomes = new ConcurrentDictionary<string, bool?>(StringComparer.Ordinal);
            XamlNamespaceScanResult namespaces = XamlNamespaceScanner.Scan(ReadText(buffer, textLength));
            bool hasPolicyNamespaces = XamlNamespacePolicies.MayAffect(namespaces, _targetPlatform, Pin(_evaluateCondition, outcomes));
            state = new DocumentState(
                textVersion,
                textLength,
                generation,
                answersVersion,
                namespaces,
                outcomes,
                hasPolicyNamespaces ? new ConcurrentDictionary<object, XamlPrefixDisposition>(ReferenceComparer.Instance) : null
            );
            Volatile.Write(ref cache.State, state);
        }

        return state;
    }

    /// <summary>
    /// Returns an evaluator that answers a question once and gives the same answer afterwards.
    /// </summary>
    /// <remarks>
    /// The answer to a condition may change while a document is being built, when the surface answers. The
    /// element and the attributes written under one prefix are judged at different moments, and judging one
    /// of them differently from the others builds an element without a property it needs. The two prefixes of
    /// an if/else pair, <c>IsTypePresent(X)</c> and <c>IsTypeNotPresent(X)</c>, ask one question as well: the
    /// answer is kept in its positive form and negated for a negated condition, so that the two branches
    /// never come from different answers.
    /// </remarks>
    internal static XamlConditionEvaluator? Pin(XamlConditionEvaluator? evaluator, ConcurrentDictionary<string, bool?> outcomes)
    {
        if (evaluator is null)
        {
            return null;
        }

        return (condition, resolveNamespace) =>
        {
            // The same text can name another condition type where the prefix is bound differently.
            XamlConditionQuery query = XamlConditionQuery.Create(condition, resolveNamespace);

            // The first condition that asks the question gives the answer, which is kept in its positive form.
            bool? positive = outcomes.GetOrAdd(query.Key, _ => ApplyNegation(evaluator(condition, resolveNamespace), query.Condition));
            return ApplyNegation(positive, query.Condition);
        };
    }

    // The answer to a negated condition is the opposite of the answer to the question, and the other way round.
    private static bool? ApplyNegation(bool? outcome, XamlCondition condition) => outcome.HasValue && condition.IsNegated ? !outcome.Value : outcome;

    private static string? ReadText(IMarkupTextBuffer buffer, int length)
    {
        try
        {
            return buffer.GetText(0, length);
        }
        catch (Exception ex)
        {
            // The text changed while it was being read. The state built from this result is replaced on
            // the next call, which sees the new text; until then the document has no policy namespaces.
            LogFailure(ex);
            return null;
        }
    }

    /// <summary>
    /// Returns the verdict for members written under <paramref name="source"/>. A prefix that is unknown
    /// or not bound to a namespace yields <see cref="XamlPrefixDisposition.Leave"/>.
    /// </summary>
    public XamlPrefixDisposition Classify(XamlSourcePrefix source) => Classify(source, _evaluateCondition);

    private XamlPrefixDisposition Classify(XamlSourcePrefix source, DocumentState state) => Classify(source, Pin(_evaluateCondition, state.Outcomes));

    private XamlPrefixDisposition Classify(XamlSourcePrefix source, XamlConditionEvaluator? evaluator)
    {
        // A member whose prefix could not be read, or is not declared, is left alone.
        if (!source.IsKnown || source.Namespace is null)
        {
            return XamlPrefixDisposition.Leave;
        }

        XamlNamespaceUri uri = source.Uri;
        Func<string, bool?>? evaluate = null;
        if (evaluator is not null && uri.IsConditional)
        {
            evaluate = condition => evaluator(condition, source.ResolveNamespace);
        }

        return XamlNamespacePolicies.Classify(source.Prefix, uri, source.IsIgnorable, _targetPlatform, evaluate);
    }

    /// <summary>
    /// Holds the state of one document; the state is replaced as a whole when the text or the policy inputs change.
    /// </summary>
    private sealed class DocumentCache
    {
        public DocumentState? State;
    }

    /// <summary>
    /// What is known about one text of a document.
    /// </summary>
    private sealed class DocumentState(
        int textVersion,
        int textLength,
        int generation,
        int answersVersion,
        XamlNamespaceScanResult namespaces,
        ConcurrentDictionary<string, bool?> outcomes,
        ConcurrentDictionary<object, XamlPrefixDisposition>? verdicts)
    {
        public int TextVersion { get; } = textVersion;

        public int TextLength { get; } = textLength;

        public int Generation { get; } = generation;

        // The answers the verdicts of this state were reached with; see Refresh.
        public int AnswersVersion { get; } = answersVersion;

        public XamlNamespaceScanResult Namespaces { get; } = namespaces;

        // The answer of each question that was asked while this state was current; see Pin.
        public ConcurrentDictionary<string, bool?> Outcomes { get; } = outcomes;

        // Keyed by source reference, of which there is one per element and per attribute in the text.
        // Null when no namespace of the document is subject to a policy: nothing needs to be looked up then.
        public ConcurrentDictionary<object, XamlPrefixDisposition>? Verdicts { get; } = verdicts;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static ReferenceComparer Instance { get; } = new();

        bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

        int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>
/// A place in a document that a policy judges: an element, or a property of an element.
/// </summary>
internal readonly struct XamlVerdictPlace(DocumentCompositeNode node, IProperty? property) : IEquatable<XamlVerdictPlace>
{
    /// <summary>
    /// Gets the element, or the element that has the property.
    /// </summary>
    public DocumentCompositeNode Node { get; } = node;

    /// <summary>
    /// Gets the property, or <see langword="null"/> for the element itself.
    /// </summary>
    public IProperty? Property { get; } = property;

    /// <inheritdoc/>
    // A node is itself, whatever Equals says about the nodes of a document.
    public bool Equals(XamlVerdictPlace other) => ReferenceEquals(Node, other.Node) && Equals(Property, other.Property);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is XamlVerdictPlace other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (RuntimeHelpers.GetHashCode(Node) * 397) ^ (Property?.GetHashCode() ?? 0);
}
