// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Windows.Foundation;

namespace WinUIDesigner.Surface.Services;

internal sealed partial class SurfaceService
{
    private const int _FreezeCompositionTimeoutMilliseconds = 200;
    // The freeze only saves rendering work: a busy UI thread is waited for this long before the freeze is just queued.
    private static readonly TimeSpan _FreezeStateTimeout = TimeSpan.FromSeconds(2);
    private static readonly Size _DefaultSurfaceSize = new(800, 600);
    private const string _XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string _BlendDesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    private const string _XSurfUwpNamespace = "using:XSurfUwp";
    private readonly Dictionary<int, DesignerSurface> _surfaces = [];
    private readonly SemaphoreSlim _constructionGate = new(1, 1);
    private readonly Dictionary<int, ResourceDictionary> _resourceDocuments = [];
    private readonly Dictionary<int, HashSet<int>> _documentResources = [];
    // Application and design-time resource dictionaries outlive individual target surfaces.
    private readonly HashSet<int> _sharedResourceDocuments = [];
    private readonly Dictionary<int, SurfaceBoundsSnapshot> _publishedSurfaceBounds = [];
    private readonly VisualTreeService _visualTreeService;
    private ResourceDictionary? _xamlControlsResources;
    private int? _applicationResourceDocumentId;
    private int? _designTimeResourceDocumentId;
    private bool _hasXamlControlsResources;
    private ElementTheme _appRequestedTheme = ElementTheme.Default;

    private async Task<CreateSurfaceResponseInfo> HandleCreateSurfaceAsync(CreateSurfaceRequestInfo requestInfo)
    {
        try
        {
            // No time limit: Visual Studio waits for this document as long as the construction before it takes.
            await DispatcherOperation.WaitWithoutLimitAsync(
                $"CreateSurface {requestInfo.Document?.DocumentId}: construction gate",
                _constructionGate.WaitAsync(Timeout.InfiniteTimeSpan, _protocolHandler.CancellationToken)
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new()
            {
                HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                Error = ex.ToString()
            };
        }

        CreateDocumentInfo? document = null;

        try
        {
            document = requestInfo.Document;
            if (document is null || document.DocumentId == 0)
            {
                throw new ArgumentException("A nonzero document ID is required.");
            }

            await DispatcherOperation.RunAsync(_dispatcherQueue, $"CreateSurface {document.DocumentId}: close the previous surface", () =>
            {
                CloseSurfaceDocument(document.DocumentId);
                return true;
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);

            var resourceDocumentsToBuild = new List<CreateDocumentInfo>();
            var resourceDocumentIds = new HashSet<int>();
            foreach (CreateDocumentInfo? resourceDocument in new[] { requestInfo.Application, requestInfo.DesignTimeResources })
            {
                if (resourceDocument is null || !resourceDocumentIds.Add(resourceDocument.DocumentId))
                {
                    continue;
                }

                resourceDocumentsToBuild.Add(resourceDocument);
            }

            await DispatcherOperation.RunAsync(_dispatcherQueue, $"CreateSurface {document.DocumentId}: application resources", () =>
            {
                foreach (CreateDocumentInfo resourceDocument in resourceDocumentsToBuild)
                {
                    InvalidateResourceDocument(resourceDocument.DocumentId);
                    ReleaseDocument(resourceDocument.DocumentId);

                    if (!_documentResources.TryGetValue(document.DocumentId, out var resourceIds))
                    {
                        _documentResources[document.DocumentId] = resourceIds = [];
                    }

                    resourceIds.Add(resourceDocument.DocumentId);
                }

                if (requestInfo.Application is not null)
                {
                    string? appXaml = TryReadPreparedXaml(requestInfo.Application) ?? requestInfo.Application.InitialXamlContent;
                    XDocument? appDocument = string.IsNullOrWhiteSpace(appXaml) ? null : XDocument.Parse(appXaml);

                    _hasXamlControlsResources = appDocument?.Root?.DescendantsAndSelf()
                        .Any(element => element.Name.LocalName == "XamlControlsResources"
                            && element.Name.NamespaceName == "using:Microsoft.UI.Xaml.Controls") == true;

                    string? requestedTheme = (string?)appDocument?.Root?.Attribute("RequestedTheme");
                    _appRequestedTheme = Enum.TryParse(requestedTheme, true, out ElementTheme theme)
                        ? theme
                        : ElementTheme.Default;
                    _applicationResourceDocumentId = requestInfo.Application.DocumentId;
                }

                LoadAppResourcesIfNeeded();
                foreach (DesignerSurface surface in _surfaces.Values)
                {
                    surface.SetRequestedTheme(_appRequestedTheme);
                }

                return true;
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);

            foreach (CreateDocumentInfo resourceDocument in resourceDocumentsToBuild)
            {
                var resources = await BuildDocumentAsync(resourceDocument, resources: true).ConfigureAwait(false);
                await DispatcherOperation.RunAsync(_dispatcherQueue, $"CreateSurface {document.DocumentId}: merge resource document {resourceDocument.DocumentId}", () =>
                {
                    ResourceDictionary dictionary = resources.Root switch
                    {
                        Application application => application.Resources,
                        ResourceDictionary value => value,
                        _ => throw new InvalidOperationException("The resource document did not construct a ResourceDictionary."),
                    };

                    IList<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
                    if (requestInfo.Application?.DocumentId == resourceDocument.DocumentId
                        && _designTimeResourceDocumentId is int designResourcesId
                        && _resourceDocuments.TryGetValue(designResourcesId, out ResourceDictionary? designResources))
                    {
                        int designResourcesIndex = mergedDictionaries.IndexOf(designResources);
                        if (designResourcesIndex >= 0)
                        {
                            mergedDictionaries.Insert(designResourcesIndex, dictionary);
                        }
                        else
                        {
                            mergedDictionaries.Add(dictionary);
                        }
                    }
                    else
                    {
                        mergedDictionaries.Add(dictionary);
                    }

                    _resourceDocuments.Add(resourceDocument.DocumentId, dictionary);
                    _sharedResourceDocuments.Add(resourceDocument.DocumentId);

                    if (requestInfo.DesignTimeResources?.DocumentId == resourceDocument.DocumentId)
                    {
                        _designTimeResourceDocumentId = resourceDocument.DocumentId;
                    }

                    return true;
                }, _protocolHandler.CancellationToken).ConfigureAwait(false);
            }

            var result = await BuildDocumentAsync(document, resources: false).ConfigureAwait(false);

            return await DispatcherOperation.RunAsync(_dispatcherQueue, $"CreateSurface {document.DocumentId}: create the surface", () =>
            {
                try
                {
                    int documentId = requestInfo.Document?.DocumentId ?? 0;
                    using var scope = _objectIdentity.EnterDocument(documentId);
                    FrameworkElement root = result.Root as FrameworkElement
                        ?? throw new InvalidOperationException("The document root must be a WinUI FrameworkElement.");
                    XamlRuntimeUtilities.RegisterNameScope(root);
                    var surface = new DesignerSurface(root, _DefaultSurfaceSize.Width, _DefaultSurfaceSize.Height);
                    surface.BoundsInvalidated += (_, _) =>
                    {
                        if (_surfaces.TryGetValue(documentId, out DesignerSurface? currentSurface)
                            && ReferenceEquals(currentSurface, surface))
                        {
                            PublishSurfaceBounds(documentId, surface);
                        }
                    };
                    surface.SurfaceLayoutUpdated += (_, _) => PublishSurfaceLayoutUpdatedIfCurrent(documentId, surface);
                    surface.DpiChanged += dpi => _protocolHandler.PostMessage((int)DesignerMessageId.SurfaceDpiChanged, new SurfaceDpiChangedEvent { DocumentId = documentId, SurfaceDpi = dpi });
                    surface.SetRequestedTheme(_appRequestedTheme);
                    _surfaces[documentId] = surface;

                    _visualTreeService.RegisterSourceInfo(requestInfo.Document, result.PreparedXaml, root);

                    long dispatcherHandle = _objectIdentity.GetHandle(_dispatcherQueue);
                    long rootVisualHandle = _objectIdentity.GetHandle(root);
                    _visualTreeService.PublishMutation(root, VisualMutationType.Add);
                    PublishSurfaceBounds(documentId, surface);
                    _ = _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                    {
                        if (!_surfaces.TryGetValue(documentId, out var current) || !ReferenceEquals(current, surface))
                        {
                            return;
                        }

                        using var scope = _objectIdentity.EnterDocument(documentId);
                        _visualTreeService.PublishMutation(root, VisualMutationType.Add);
                        _protocolHandler.PostMessage((int)DesignerMessageId.OnApplicationEvent, new OnApplicationEventResponse { EventName = "OnIdle" });
                    });

                    return new CreateSurfaceResponseInfo { DispatcherHandle = dispatcherHandle, RootVisualHandle = rootVisualHandle };
                }
                catch
                {
                    // Let the construction failure path release resources and identities,
                    // including a surface installed before a later initialization failure.
                    throw;
                }
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WinUIDesignerLogger.LogError("Surface", $"CreateSurface failed for document {document?.DocumentId}.", ex);

            if (ex is DocumentConstructionException constructionError)
            {
                _protocolHandler.PostMessage(
                    (int)DesignerMessageId.InstanceBuildingErrors,
                    new InstanceBuildingErrors()
                    {
                        DocumentId = document?.DocumentId ?? 0,
                        Errors = constructionError.SerializedErrors
                    });
            }
            else
            {
                _protocolHandler.PostMessage(
                    (int)DesignerMessageId.UnhandledException,
                    new UnhandledExceptionResponse()
                    {
                        DocumentId = document?.DocumentId ?? 0,
                        Message = ex.Message,
                        CallStack = ex.ToString(),
                        IsArtboardException = true,
                    });
            }
            try
            {
                await DispatcherOperation.RunAsync(_dispatcherQueue, $"CreateSurface {document?.DocumentId}: clean up after the failure", () =>
                {
                    if (document is not null)
                    {
                        CloseSurfaceDocument(document.DocumentId);
                    }

                    return true;
                }, _protocolHandler.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                WinUIDesignerLogger.LogWarning("Surface", "Surface cleanup after a failed operation also failed.", cleanupException);
            }

            return new()
            {
                HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                Error = ex.ToString()
            };
        }
        finally
        {
            _constructionGate.Release();
        }
    }

    private async Task<ResponseWithError> HandleCloseDocumentAsync(CloseDocumentRequestInfo request)
    {
        try
        {
            // No time limit: Visual Studio only posts this message, so a close that gave up would never be done.
            await DispatcherOperation.WaitWithoutLimitAsync(
                $"CloseDocument {request.DocumentId}: construction gate",
                _constructionGate.WaitAsync(Timeout.InfiniteTimeSpan, _protocolHandler.CancellationToken)
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new()
            {
                HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                Error = ex.ToString()
            };
        }
        try
        {
            return await DispatcherOperation.RunAsync(_dispatcherQueue, $"CloseDocument {request.DocumentId}", () =>
            {
                CloseSurfaceDocument(request.DocumentId);
                return new ResponseWithError() { HResult = 0 };
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Surface", "Surface operation failed.", exception);
            return new ResponseWithError
            {
                HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
                Error = exception.ToString()
            };
        }
        finally { _constructionGate.Release(); }
    }

    private async Task<ResponseWithError> HandleSetPanZoomTransformAsync(SetPanZoomTransformRequestInfo request)
    {
        try
        {
            return await DispatcherOperation.RunAsync(_dispatcherQueue, $"SetPanZoomTransform {request.DocumentId}", () =>
            {
                if (!_surfaces.TryGetValue(request.DocumentId, out DesignerSurface? surface))
                {
                    return new ResponseWithError { HResult = NativeMethods.E_INVALIDARG };
                }

                surface.SetPanZoomTransform(request.OffsetX, request.OffsetY, request.Scale);
                return new ResponseWithError { HResult = 0 };
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Surface", "Surface operation failed.", exception);
            return new ResponseWithError
            {
                HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
                Error = exception.ToString()
            };
        }
    }

    private async Task<ResponseWithError> HandleSetArtboardColorsAsync(SetArtboardColorsRequestInfo request)
    {
        static bool TryParseColor(string? value, out Windows.UI.Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = value.Trim().TrimStart('#');
            if (text.Length == 6)
            {
                text = "FF" + text;
            }
            if (text.Length != 8 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint argb))
            {
                return false;
            }

            color = Windows.UI.Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            return true;
        }

        try
        {
            return await DispatcherOperation.RunAsync(_dispatcherQueue, $"SetArtboardColors {request.DocumentId}", () =>
            {
                if (!_surfaces.TryGetValue(request.DocumentId, out DesignerSurface? surface))
                {
                    return new ResponseWithError { HResult = NativeMethods.E_INVALIDARG };
                }

                if (TryParseColor(request.CheckerboardColor1, out Windows.UI.Color color1)
                    && TryParseColor(request.CheckerboardColor2, out Windows.UI.Color color2))
                {
                    surface.SetCheckerboardColors(color1, color2);
                }

                return new ResponseWithError { HResult = 0 };
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Surface", "Surface operation failed.", exception);
            return new ResponseWithError
            {
                HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
                Error = exception.ToString()
            };
        }
    }

    private async Task<ResponseWithError> HandleSetDeviceSizeAsync(SetDeviceSizeRequestInfo request)
    {
        try
        {
            return await DispatcherOperation.RunAsync(_dispatcherQueue, $"SetDeviceSize {request.DocumentId}", () =>
            {
                if (!_surfaces.TryGetValue(request.DocumentId, out DesignerSurface? surface))
                {
                    return new ResponseWithError { HResult = NativeMethods.E_INVALIDARG };
                }

                surface.SetDeviceSize(request.Width, request.Height);
                PublishSurfaceBounds(request.DocumentId, surface);
                return new ResponseWithError { HResult = 0 };
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Surface", "Surface operation failed.", exception);
            return new ResponseWithError
            {
                HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
                Error = exception.ToString()
            };
        }
    }

    private void HandleSetFreezeState(SetFreezeStateInfo request)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            ApplyFreezeState(request.DocumentIdToFreeze);
            return;
        }

        DesignerSurface? targetSurface = null;
        bool waitForPreviousUnfreeze = false;
        try
        {
            // Visual Studio only posts this message, and the protocol takes its next message only after this one, so a
            // busy UI thread is waited for only briefly: then the freeze is queued without waiting for the previous
            // unfreeze to be drawn.
            bool inspected = DispatcherOperation.TryInvoke(_dispatcherQueue, () =>
            {
                if (request.DocumentIdToFreeze != 0 &&
                    _surfaces.TryGetValue(request.DocumentIdToFreeze, out DesignerSurface? surface))
                {
                    targetSurface = surface;
                    waitForPreviousUnfreeze = !surface.IsFrozen;
                }

                return true;
            }, _FreezeStateTimeout, _protocolHandler.CancellationToken, out _);
            if (inspected && waitForPreviousUnfreeze && targetSurface is not null)
            {
                targetSurface.WaitForUnfreezeComposition(_FreezeCompositionTimeoutMilliseconds);
            }
        }
        catch (Exception exception)
        {
            // Nothing may leave a synchronous observer: the protocol's thread, and with it the process, would end.
            WinUIDesignerLogger.LogWarning("Surface", $"SetFreezeState {request.DocumentIdToFreeze}: the surface to freeze could not be inspected.", exception);
        }

        // Queued, not waited for: it runs after the work that is already on the UI thread and in the order the messages
        // came, so that neither a freeze nor the unfreeze after it is lost.
        _ = ApplyFreezeStateAsync(request.DocumentIdToFreeze);
    }

    private async Task ApplyFreezeStateAsync(int documentIdToFreeze)
    {
        try
        {
            await DispatcherOperation.RunAsync(_dispatcherQueue, $"SetFreezeState {documentIdToFreeze}", () =>
            {
                ApplyFreezeState(documentIdToFreeze);
                return true;
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Surface", $"SetFreezeState {documentIdToFreeze} was not applied.", exception);
        }
    }

    private async Task<ResponseWithError> HandleSetSurfacePositionAsync(SetSurfacePositionRequestInfo request)
    {
        try
        {
            return await DispatcherOperation.RunAsync(_dispatcherQueue, $"SetSurfacePosition {request.DocumentId}", () =>
            {
                if (!_surfaces.TryGetValue(request.DocumentId, out DesignerSurface? surface))
                {
                    return new ResponseWithError { HResult = NativeMethods.E_INVALIDARG };
                }

                using var documentScope = _objectIdentity.EnterDocument(request.DocumentId);
                surface.SetSurfacePosition(new IntPtr(request.ParentWindow), request.Width, request.Height);
                LoadAppResourcesIfNeeded();
                _visualTreeService.PublishMutation(surface.Content, VisualMutationType.Add);
                PublishSurfaceBounds(request.DocumentId, surface);

                return new ResponseWithError { HResult = 0 };
            }, _protocolHandler.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogError("Surface", "Surface operation failed.", exception);
            return new ResponseWithError
            {
                HResult = exception.HResult != 0 ? exception.HResult : Marshal.GetHRForException(exception),
                Error = exception.ToString()
            };
        }
    }

    private async Task<(object Root, string? PreparedXaml)> BuildDocumentAsync(CreateDocumentInfo document, bool resources)
    {
        Exception? parseFailure = null;
        if (!document.HasActions)
        {
            try
            {
                return await DispatcherOperation.RunAsync(_dispatcherQueue, $"Build document {document.DocumentId}: prepared XAML", () =>
                {
                    using var scope = _objectIdentity.EnterDocument(document.DocumentId);
                    string xaml = TryReadPreparedXaml(document) ?? document.InitialXamlContent;
                    if (string.IsNullOrWhiteSpace(xaml))
                    {
                        throw new InvalidOperationException("The prepared document has no XAML.");
                    }

                    string runtimeXaml = xaml;
                    if (resources)
                    {
                        XDocument resourceDocument = XDocument.Parse(runtimeXaml);
                        XElement resourceRoot = resourceDocument.Root
                            ?? throw new InvalidOperationException("The resource document is empty.");
                        if (resourceRoot.Name.LocalName == "Application")
                        {
                            XElement? property = resourceRoot.Elements().FirstOrDefault(value => value.Name.LocalName == "Application.Resources");
                            XElement dictionary = property?.Elements().FirstOrDefault(value => value.Name.LocalName == "ResourceDictionary")
                                ?? new XElement(resourceRoot.Name.Namespace + "ResourceDictionary", property?.Elements() ?? []);
                            foreach (XAttribute declaration in resourceRoot.Attributes().Where(value => value.IsNamespaceDeclaration))
                            {
                                if (dictionary.Attribute(declaration.Name) is null)
                                {
                                    dictionary.Add(new XAttribute(declaration));
                                }
                            }

                            runtimeXaml = dictionary.ToString();
                        }
                    }

                    XDocument cleanedXaml = XDocument.Parse(runtimeXaml, LoadOptions.PreserveWhitespace);
                    XNamespace x = _XamlNamespace;
                    cleanedXaml.Root?.Attribute(x + "Class")?.Remove();

                    if (cleanedXaml.Root is not null)
                    {
                        foreach (XElement element in cleanedXaml.Root.DescendantsAndSelf())
                        {
                            // The UWP cleaner can suppress project code and emit its own stand-in.
                            // Keep that decision, but use a WinUI control instead of UwpSurface.
                            if (element.Name.NamespaceName == "using:XSurfUwp.Fallback"
                                && ("XSurfUwp.Fallback." + element.Name.LocalName is "XSurfUwp.Fallback.FallbackControl" or "XSurfUwp.Fallback.LayoutFaultFallbackControl"))
                            {
                                element.Name = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation") + "ContentControl";
                                if (element.Attribute("Content") is null && element.Attribute("Tag") is { } originalType)
                                {
                                    element.SetAttributeValue("Content", originalType.Value);
                                }
                            }

                            foreach (XAttribute attribute in new List<XAttribute>(element.Attributes()))
                            {
                                if (!attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName == _XSurfUwpNamespace)
                                {
                                    if (attribute.Name.LocalName is not (
                                        "DT.RootWidth" or "DT.DesignWidth" or "DT.RuntimeWidth" or
                                        "DT.RootHeight" or "DT.DesignHeight" or "DT.RuntimeHeight"))
                                    {
                                        attribute.Remove();
                                    }

                                    continue;
                                }

                                if (attribute.IsNamespaceDeclaration && attribute.Value == _XSurfUwpNamespace)
                                {
                                    if (attribute.Name.LocalName == "d")
                                    {
                                        attribute.Value = _BlendDesignNamespace;
                                    }
                                }
                            }
                        }
                    }

                    string sanitizedXaml = cleanedXaml.Root is null
                        ? runtimeXaml
                        : cleanedXaml.ToString(SaveOptions.DisableFormatting);
                    object root = XamlReader.Load(sanitizedXaml);
                    _objectIdentity.Track(root);
                    return (root, (string?)xaml);
                }, _protocolHandler.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!DispatcherOperation.IsUnanswered(ex))
            {
                // Only a load that failed is retried with construction actions. A load that was cancelled or gave up is
                // no parse failure, and Visual Studio marks a document whose actions it handed out as built from them.
                parseFailure = ex;
                WinUIDesignerLogger.LogTrace("Surface", $"Prepared XAML parsing failed for document {document.DocumentId}; requesting construction actions: {ex}");
            }
        }

        CreateDocumentInfo actionsDocument = document;
        if (!document.HasActions)
        {
            // No time limit: Visual Studio builds the actions on its UI thread, as long as that takes.
            actionsDocument = await DispatcherOperation.WaitWithoutLimitAsync(
                $"GetDocumentBuildingActions {document.DocumentId}",
                _protocolHandler.SendMessageAsync<CreateDocumentInfo>((int)DesignerMessageId.GetDocumentBuildingActions,
                    new GetDocumentBuildingActionsRequest { DocumentId = document.DocumentId })
                    .WaitAsync(_protocolHandler.CancellationToken)
            ).ConfigureAwait(false);
        }

        if (actionsDocument is null || actionsDocument.DocumentId != document.DocumentId || !actionsDocument.HasActions)
        {
            throw new InvalidOperationException($"No construction actions were returned for document {document.DocumentId}.", parseFailure);
        }

        return await DispatcherOperation.RunAsync(_dispatcherQueue, $"Build document {document.DocumentId}: construction actions", () =>
        {
            if (!resources)
            {
                return (BuildDocument(actionsDocument), (string?)null);
            }
            // Isolate action-built App resources from the designer's own dictionary.

            ResourceDictionary previous = Application.Current.Resources;
            Application.Current.Resources = [];
            try
            {
                object root = BuildDocument(actionsDocument);
                return (root is Application app ? (object)app.Resources : root, (string?)null);
            }
            finally
            {
                Application.Current.Resources = previous;
            }
        }, _protocolHandler.CancellationToken).ConfigureAwait(false);
    }

    private void CloseSurfaceDocument(int documentId)
    {
        // VS can close an application/resource document directly after an edit.
        // Keeping it in the cache would reuse its old dictionary on the next build.
        InvalidateResourceDocument(documentId);

        if (_documentResources.Remove(documentId, out var resourceIds))
        {
            foreach (int resourceId in resourceIds)
            {
                if (_sharedResourceDocuments.Contains(resourceId))
                {
                    continue;
                }

                if (_documentResources.Values.Any(ids => ids.Contains(resourceId)))
                {
                    continue;
                }

                InvalidateResourceDocument(resourceId);
                ReleaseDocument(resourceId);
            }
        }
        if (_surfaces.Remove(documentId, out DesignerSurface? surface))
        {
            _visualTreeService.PublishMutation(surface.Content, VisualMutationType.Remove);
            surface.Dispose();
        }
        _publishedSurfaceBounds.Remove(documentId);
        ReleaseDocument(documentId);
    }

    private void InvalidateResourceDocument(int resourceDocumentId)
    {
        if (_resourceDocuments.Remove(resourceDocumentId, out var dictionary))
        {
            Application.Current.Resources.MergedDictionaries.Remove(dictionary);
        }

        _sharedResourceDocuments.Remove(resourceDocumentId);
        foreach (var ids in _documentResources.Values)
        {
            ids.Remove(resourceDocumentId);
        }

        if (_applicationResourceDocumentId == resourceDocumentId)
        {
            _applicationResourceDocumentId = null;
            _hasXamlControlsResources = false;
            _appRequestedTheme = ElementTheme.Default;
            LoadAppResourcesIfNeeded();

            foreach (DesignerSurface surface in _surfaces.Values)
            {
                surface.SetRequestedTheme(_appRequestedTheme);
            }
        }

        if (_designTimeResourceDocumentId == resourceDocumentId)
        {
            _designTimeResourceDocumentId = null;
        }
    }

    private void SetSurfaceContent(int documentId, object? content)
    {
        if (content is not FrameworkElement root)
        {
            throw new InvalidOperationException($"Surface content for document {documentId} must be a FrameworkElement, got {content?.GetType().FullName ?? "<null>"}.");
        }

        if (!_surfaces.TryGetValue(documentId, out DesignerSurface? surface))
        {
            XamlRuntimeUtilities.RegisterNameScope(root);
            surface = new DesignerSurface(root, _DefaultSurfaceSize.Width, _DefaultSurfaceSize.Height);
            surface.BoundsInvalidated += (_, _) =>
            {
                if (_surfaces.TryGetValue(documentId, out DesignerSurface? currentSurface)
                    && ReferenceEquals(currentSurface, surface))
                {
                    PublishSurfaceBounds(documentId, surface);
                }
            };
            surface.SurfaceLayoutUpdated += (_, _) => PublishSurfaceLayoutUpdatedIfCurrent(documentId, surface);
            surface.DpiChanged += dpi => _protocolHandler.PostMessage((int)DesignerMessageId.SurfaceDpiChanged, new SurfaceDpiChangedEvent { DocumentId = documentId, SurfaceDpi = dpi });
            surface.SetRequestedTheme(_appRequestedTheme);
            _surfaces[documentId] = surface;
        }
        else if (!ReferenceEquals(surface.Content, root))
        {
            XamlRuntimeUtilities.RegisterNameScope(root);
            FrameworkElement oldRoot = surface.Content;
            _visualTreeService.PublishMutation(oldRoot, VisualMutationType.Remove);
            surface.ReplaceContent(root);
            _visualTreeService.RemoveIdentities(oldRoot);
        }
        else
        {
            surface.RefreshLayout();
        }

        _objectIdentity.GetHandle(root);
        _visualTreeService.PublishMutation(root, VisualMutationType.Add);
        PublishSurfaceBounds(documentId, surface);
    }

    private void CompleteActionBatch()
    {
        foreach (var entry in _surfaces)
        {
            using var scope = _objectIdentity.EnterDocument(entry.Key);
            entry.Value.RefreshLayout();
            _visualTreeService.PublishMutationIfChanged(entry.Value.Content);
            PublishSurfaceBounds(entry.Key, entry.Value);
        }
    }

    private static string? TryReadPreparedXaml(CreateDocumentInfo? document)
    {
        if (string.IsNullOrWhiteSpace(document?.DesignTimeSurfaceUri))
        {
            return null;
        }

        string path = document.DesignTimeSurfaceUri;
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri))
        {
            if (uri.IsFile)
            {
                path = uri.LocalPath;
            }
            else if (string.Equals(uri.Scheme, "ms-appx", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.Combine(
                    Program.ContentDirectory,
                    Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            }
        }

        if (!File.Exists(path))
        {
            return null;
        }

        return File.ReadAllText(path);
    }

    private void ApplyFreezeState(int documentIdToFreeze)
    {
        foreach ((int id, DesignerSurface surface) in _surfaces)
        {
            if (id != documentIdToFreeze && surface.IsFrozen)
            {
                surface.Unfreeze();
            }
        }
        // Rendering suspension belongs to the UI thread, so resume the old
        // document before freezing the next one, regardless of dictionary order.

        if (documentIdToFreeze != 0
            && _surfaces.TryGetValue(documentIdToFreeze, out DesignerSurface? targetSurface)
            && !targetSurface.IsFrozen)
        {
            targetSurface.Freeze();
        }
    }

    private void LoadAppResourcesIfNeeded()
    {
        if (!_hasXamlControlsResources)
        {
            if (_xamlControlsResources is not null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(_xamlControlsResources);
                _xamlControlsResources = null;
            }

            return;
        }

        if (_xamlControlsResources is not null)
        {
            return;
        }

        _xamlControlsResources = new XamlControlsResources();
        IList<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
        if (_designTimeResourceDocumentId is int designResourcesId
            && _resourceDocuments.TryGetValue(designResourcesId, out ResourceDictionary? designResources))
        {
            int designResourcesIndex = mergedDictionaries.IndexOf(designResources);
            if (designResourcesIndex >= 0)
            {
                mergedDictionaries.Insert(designResourcesIndex, _xamlControlsResources);
            }
            else
            {
                mergedDictionaries.Add(_xamlControlsResources);
            }
        }
        else
        {
            mergedDictionaries.Add(_xamlControlsResources);
        }

    }

    private void PublishSurfaceLayoutUpdatedIfCurrent(int documentId, DesignerSurface surface)
    {
        if (!_surfaces.TryGetValue(documentId, out DesignerSurface? currentSurface)
            || !ReferenceEquals(currentSurface, surface))
        {
            return;
        }

        _protocolHandler.PostMessage((int)DesignerMessageId.SurfaceLayoutUpdated, new SurfaceLayoutUpdatedEvent
        {
            DocumentId = documentId,
        });
    }

    private void PublishSurfaceBounds(int documentId, DesignerSurface surface)
    {
        Rect contentBounds = surface.GetContentBounds();
        Rect documentBounds = surface.GetDocumentBounds();
        var currentBounds = new SurfaceBoundsSnapshot(contentBounds, documentBounds);
        if (_publishedSurfaceBounds.TryGetValue(documentId, out SurfaceBoundsSnapshot publishedBounds)
            && publishedBounds.Equals(currentBounds))
        {
            return;
        }

        _publishedSurfaceBounds[documentId] = currentBounds;
        _protocolHandler.PostMessage((int)DesignerMessageId.SurfaceBoundsChanged, new SurfaceBoundsChangedEventContract
        {
            DocumentId = documentId,
            ContentBounds = new RectContract { X = contentBounds.X, Y = contentBounds.Y, Width = contentBounds.Width, Height = contentBounds.Height },
            DocumentBounds = new RectContract { X = documentBounds.X, Y = documentBounds.Y, Width = documentBounds.Width, Height = documentBounds.Height },
        });
    }
}
