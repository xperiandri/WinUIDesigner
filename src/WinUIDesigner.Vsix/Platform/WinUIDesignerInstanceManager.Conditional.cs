// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using WinUIDesigner.Markup;
using WinUIDesigner.Protocol;

namespace WinUIDesigner.Platform;

internal sealed partial class WinUIDesignerInstanceManager
{
    // The surface answers in the order it receives messages, and conditions of the application are created
    // on its UI thread. Loading a document must not wait long for either.
    private static readonly TimeSpan _ConditionAnswerTimeout = TimeSpan.FromSeconds(2);

    // A surface process that has just started answers once it is ready. That took about six seconds in a
    // running designer and, on a machine that was busy with scanning for malware, up to about a minute. A
    // document whose conditions only the surface can decide waits for the same start anyway. The wait is one
    // stretch of time for all documents and is spent once, also when the surface never answers; a trace says
    // after twenty seconds that the surface is still starting.
    private static readonly TimeSpan _FirstConditionAnswerTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan _FirstConditionAnswerNotice = TimeSpan.FromSeconds(20);

    private static readonly object _Reported = new();

    // Places in the source text that were looked at for a lost value already; the keys die with their documents.
    private readonly ConditionalWeakTable<object, object> _reportedLostValues = new();

    private XamlConditionAnswers? _conditionAnswers;

    /// <summary>
    /// Gets the answers of the surface process to the conditions of conditional XAML namespaces. Creating them
    /// makes them the evaluator of the member dispositions; until the surface answers they evaluate nothing.
    /// </summary>
    private XamlConditionAnswers ConditionAnswers => LazyInitializer.EnsureInitialized(ref _conditionAnswers, CreateConditionAnswers)!;

    /// <summary>
    /// Prepares the evaluation of conditional XAML namespaces before a document is sent to the surface.
    /// </summary>
    /// <remarks>
    /// The surface evaluates the conditions of prepared XAML itself. Its answers are needed here for the
    /// other path: when the surface asks for construction actions, and for every edit afterwards, the
    /// document model decides which members exist. Only conditions that this process cannot decide are waited
    /// for: those about a custom condition or a type of the application.
    /// </remarks>
    private async Task PrepareConditionalNamespacesAsync(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        XamlConditionAnswers answers = ConditionAnswers;

        // Everything that may be built for this request: the target, the documents prepared with it, and
        // what else the markup provider has registered, such as App.xaml and the design-time resources.
        // The questions that come up while the documents are scanned go out together, in one request.
        var queries = new Dictionary<string, XamlConditionQuery>(StringComparer.Ordinal);
        var scanned = new HashSet<IDocumentRoot>();
        using (answers.BeginCollecting())
        {
            AddConditions(queries, scanned, targetDocument);
            foreach (ISurfaceProcessDocument document in preparedDocuments)
            {
                AddConditions(queries, scanned, document);
            }

            AddConditions(queries, scanned, MarkupProvider.AppXaml);
            foreach (ISurfaceProcessDocument document in MarkupProvider.RegisteredDocuments)
            {
                AddConditions(queries, scanned, document);
            }
        }

        // The answers that arrived take effect here, before anything of this request is built.
        await answers.AskAsync(
            new List<XamlConditionQuery>(queries.Values),
            _ConditionAnswerTimeout,
            _FirstConditionAnswerTimeout,
            _FirstConditionAnswerNotice
        ).ConfigureAwait(false);

        // The documents that are about to be built are judged with the answers that took effect. A document
        // that is being built, such as another page that loads at the same time, keeps the answers it has.
        RefreshVerdicts(targetDocument, preparedDocuments);
        if (queries.Count > 0)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Conditional XAML: the documents of this request contain {queries.Count} condition(s); the surface {(answers.SurfaceAnswers ? "answers" : "has not answered yet")}.");
        }
    }

    /// <summary>
    /// Makes the documents of this request be judged with the answers that took effect.
    /// </summary>
    /// <remarks>
    /// The other documents that the markup provider has registered are scanned for questions as well, but their
    /// verdicts are left as they are: one of them may be being built for another request right now, on another
    /// thread, and has to finish with the answers it has. It is judged with the new answers when its text
    /// changes or when a request is made for it.
    /// </remarks>
    private void RefreshVerdicts(ISurfaceProcessDocument targetDocument, IReadOnlyList<ISurfaceProcessDocument> preparedDocuments)
    {
        _memberDispositions.Refresh(targetDocument.Document);
        foreach (ISurfaceProcessDocument document in preparedDocuments)
        {
            _memberDispositions.Refresh(document.Document);
        }

        _memberDispositions.Refresh(MarkupProvider.AppXaml?.Document);
    }

    /// <summary>
    /// Reports a value the document model lost, when a property is not built because its prefix is dropped.
    /// </summary>
    /// <remarks>
    /// The document model keeps one value per property: of <c>c7:CornerRadius="4" c7n:CornerRadius="0"</c>
    /// only the last attribute is in it, and of <c>&lt;c7:Border.Background&gt;</c> followed by
    /// <c>&lt;c7n:Border.Background&gt;</c> only the last property element. When that one is dropped, the value
    /// of the member that applies is gone and the property stays unset. Prepared XAML does not have this problem.
    /// </remarks>
    private void ReportValueLostToDroppedProperty(DocumentCompositeNode parentNode, IProperty property)
    {
        try
        {
            // The builders ask about a property more than once; one report per place in the text is enough.
            object? site = parentNode.GetContainerContext(property);
            if (site is null)
            {
                return;
            }

            bool isFirstReport = false;
            _reportedLostValues.GetValue(site, _ =>
            {
                isFirstReport = true;
                return _Reported;
            });
            if (!isFirstReport)
            {
                return;
            }

            XamlSourceMember dropped = XamlSourcePrefixReader.ReadMember(parentNode, property);
            if (!dropped.IsKnown)
            {
                return;
            }

            XamlAttributeName? lost = XamlLostValueFinder.Find(
                dropped.Name,
                dropped.Members,
                prefix => dropped.ResolvePrefix(prefix) is { Namespace: not null } resolved ? resolved.Uri.BaseUri : null,
                prefix => _memberDispositions.Classify(dropped.ResolvePrefix(prefix))
            );
            if (lost is not null)
            {
                WinUIDesignerLogger.LogWarning(
                    "Platform",
                    $"The value of {lost} on <{parentNode.Type.Name}> is lost while the document is built from construction actions: "
                    + $"{dropped.Name} follows it and does not apply, and Visual Studio's document model keeps only the last value written for a property. "
                    + "The property stays unset in the preview. Writing the member that applies last avoids this."
                );
            }
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogTrace("Platform", $"Looking for a value lost to a dropped member failed: {ex}");
        }
    }

    private XamlConditionAnswers CreateConditionAnswers()
    {
        var answers = new XamlConditionAnswers(
            request => ProtocolHandler.SendMessageAsync<EvaluateXamlConditionsResponse>((int)DesignerMessageId.EvaluateXamlConditions, request),
            ProtocolHandler.CancellationToken,
            _memberDispositions.AnswersChanged,
            XamlConditionAnswers.CreateLocalProbes(),
            (message, exception) => WinUIDesignerLogger.LogWarning("Platform", message, exception),
            message => WinUIDesignerLogger.LogTrace("Platform", message)
        );

        _memberDispositions.EvaluateCondition = answers.Evaluate;
        return answers;
    }

    private void AddConditions(Dictionary<string, XamlConditionQuery> queries, HashSet<IDocumentRoot> scanned, ISurfaceProcessDocument? document)
    {
        IDocumentRoot? root = document?.Document;
        if (root is null || !scanned.Add(root))
        {
            return;
        }

        XamlNamespaceScanResult namespaces = _memberDispositions.GetNamespaces(root);
        if (!namespaces.HasConditionalNamespaces)
        {
            return;
        }

        IReadOnlyList<XamlNamespaceDeclaration> declarations = namespaces.Declarations;
        for (int index = 0; index < declarations.Count; index++)
        {
            string? condition = declarations[index].Uri.Condition;
            if (condition is null)
            {
                continue;
            }

            // The name of a custom condition resolves through a prefix. The scan does not record where a
            // declaration is in scope, so the condition is asked for every namespace the prefix is bound to
            // in the document; usually that is one.
            string prefix = XamlCondition.Parse(condition).Prefix;
            bool isDeclared = false;
            for (int candidate = 0; candidate < declarations.Count; candidate++)
            {
                if (string.Equals(declarations[candidate].Prefix, prefix, StringComparison.Ordinal))
                {
                    string predicateNamespace = declarations[candidate].Uri.Original;
                    AddQuery(queries, XamlConditionQuery.Create(condition, _ => predicateNamespace));
                    isDeclared = true;
                }
            }

            if (!isDeclared)
            {
                AddQuery(queries, XamlConditionQuery.Create(condition, resolveNamespace: null));
            }
        }
    }

    private static void AddQuery(Dictionary<string, XamlConditionQuery> queries, XamlConditionQuery query)
    {
        if (!queries.ContainsKey(query.Key))
        {
            queries.Add(query.Key, query);
        }
    }
}
