// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.InstanceBuilders.Shared;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using WinUIDesigner.Markup;

namespace WinUIDesigner.Surface.Services;

/// <summary>
/// Applies instance-builder actions to the live WinUI document tree.
/// Scopes proxy handles to each action batch and maps them to stable live-object handles.
/// </summary>
internal sealed partial class SurfaceService
{
    private readonly record struct DeferredSetterInsertion(XamlAction Action, SetterBaseCollection Collection, int Index, SetterBase Setter);

    private readonly ResourceScopeService _resourceScopes;
    private readonly StyleEditService _styleEdits;
    private readonly DroppedChildren _droppedChildren;
    private readonly Dictionary<long, object?> _actionObjects = [];
    private readonly Dictionary<long, object?> _trackedActionObjects = [];

    private ResponseWithError HandleExecuteActions(ExecuteXamlActionsRequestInfo request)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            try
            {
                return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => HandleExecuteActions(request), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return new ResponseWithError()
                {
                    HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                    Error = ex.ToString(),
                };
            }
        }

        XamlAction? failedAction = null;
        var deferredSetterInsertions = CreateDeferredSetterInsertions();

        try
        {
            var actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
            var documentId = 0;

            foreach (XamlAction action in actions)
            {
                if (action is SetDispatcherAction dispatcher && _objectIdentity.TryGetObject(dispatcher.DispatcherObjectHandle, out object? target) && target is not null)
                {
                    documentId = _objectIdentity.GetDocumentId(target);
                }

                if (action is SetSurfaceContentAction content)
                {
                    documentId = content.DocumentId;
                }

                if (documentId != 0)
                {
                    break;
                }
            }

            // Keep the wire order: later actions can refer to objects or names created
            // by earlier actions in this same transaction.
            using var documentScope = _objectIdentity.EnterDocument(documentId);
            foreach (XamlAction action in actions)
            {
                failedAction = action;
                Execute(failedAction, deferredSetterInsertions: deferredSetterInsertions);
                TrackActionObjects();
            }

            foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeAll())
            {
                failedAction = insertion.Action;
                InsertSetter(insertion);
            }

            _styleEdits.CompleteBatch(GetSurfaceRoots());
            CompleteActionBatch();

            return new ResponseWithError()
            {
                HResult = 0
            };
        }
        catch (Exception ex)
        {
            // Synchronize the surviving changes, then report the exact failed
            // action using the shared error contract. Raw exception text is not
            // valid ActionError JSON and prevents frontend error processing.
            if (ex is StyleEditException)
            {
                // StyleEditService has logged the cause once; a repeated edit must not flood the log.
                WinUIDesignerLogger.LogTrace("Surface", $"ExecuteXamlActions ({(int)DesignerMessageId.ExecuteXamlActions}) failed during '{failedAction?.Action}': {ex.Message}");
            }
            else
            {
                // Any other failure is unexpected.
                WinUIDesignerLogger.LogError("Surface", $"ExecuteXamlActions ({(int)DesignerMessageId.ExecuteXamlActions}) failed during '{failedAction?.Action}'.", ex);
            }

            try
            {
                _styleEdits.CompleteBatch(GetSurfaceRoots());
                CompleteActionBatch();
            }
            catch (Exception layoutError)
            {
                WinUIDesignerLogger.LogWarning("Surface", "Layout after failed action also failed.", layoutError);
            }

            return new ResponseWithError()
            {
                HResult = ex.HResult != 0 ? ex.HResult : Marshal.GetHRForException(ex),
                Error = failedAction is null ? ex.ToString() : ActionErrorJsonSerializer.Serialize([new() { XamlAction = failedAction, Error = ex.ToString() },]),
            };
        }
    }

    private LiveValue ExecuteLookupActions(ExecuteXamlActionsRequestInfo request, LiveValueSerializer serializer)
    {
        if (!_dispatcherQueue.HasThreadAccess)
        {
            return DispatcherOperation.InvokeAsync(_dispatcherQueue, () => ExecuteLookupActions(request, serializer), _protocolHandler.CancellationToken).GetAwaiter().GetResult();
        }

        // The actions build a value for Visual Studio to read, outside the live tree.
        using IDisposable detachedBuild = _styleEdits.EnterDetachedBuild();
        object? captured = null;
        var deferredSetterInsertions = CreateDeferredSetterInsertions();
        var actions = XamlActionJsonSerializer.Deserialize(request.Actions ?? []);
        foreach (XamlAction action in actions)
        {
            Execute(action, deferredSetterInsertions, (_, value) => captured = value);
        }

        foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeAll())
        {
            InsertSetter(insertion);
        }

        // A lookup that ends at a dropped element finds nothing: the element has no object.
        return serializer.Serialize(captured is DroppedElement ? null : captured);
    }

    private object BuildDocument(CreateDocumentInfo document)
    {
        using var scope = _objectIdentity.EnterDocument(document.DocumentId);

        // The document joins a surface only after it is built.
        using IDisposable detachedBuild = _styleEdits.EnterDetachedBuild();
        object? root = null;
        var deferredSetterInsertions = CreateDeferredSetterInsertions();
        var actions = XamlActionJsonSerializer.Deserialize(document.Actions ?? []);

        for (int index = 0; index < actions.Count; index++)
        {
            XamlAction action = actions[index];

            try
            {
                Execute(action, deferredSetterInsertions, (id, value) =>
                {
                    if (id != document.DocumentId)
                    {
                        throw new InvalidOperationException("The construction action targets another document.");
                    }

                    root = value;
                });

                TrackActionObjects();
            }
            catch (Exception ex)
            {
#if DEBUG
                if (WinUIDesignerLogger.IsVerboseLoggingEnabled
                    && document.Actions is { } serializedActions
                    && index < serializedActions.Count)
                {
                    WinUIDesignerLogger.LogTrace("Surface", $"BuildDocument ({document.DocumentId}) failed at action[{index}]: {serializedActions[index]}", ex);
                }
#endif
                throw new DocumentConstructionException(ex, action);
            }
        }

        foreach (DeferredSetterInsertion insertion in deferredSetterInsertions.TakeAll())
        {
            try
            {
                InsertSetter(insertion);
            }
            catch (Exception ex)
            {
                throw new DocumentConstructionException(ex, insertion.Action);
            }
        }

        return root ?? throw new InvalidOperationException($"No root was constructed for document {document.DocumentId}.");
    }

    private static DeferredInsertionQueue<DeferredSetterInsertion> CreateDeferredSetterInsertions()
    {
        return new(insertion => insertion.Collection);
    }

    // Visual Studio sets a Setter's Property and Value after it adds the setter. Once they are set the setter is
    // valid, and an insertion into a sealed collection through XAML Diagnostics also reaches styles BasedOn it.
    private void InsertSetter(DeferredSetterInsertion insertion)
    {
        if (!_styleEdits.TryInsertSetter(insertion.Collection, insertion.Index, insertion.Setter))
        {
            XamlRuntimeUtilities.AddChild(insertion.Collection, insertion.Index, insertion.Setter);
        }
    }

    private List<FrameworkElement> GetSurfaceRoots()
    {
        var roots = new List<FrameworkElement>(_surfaces.Count);
        foreach (DesignerSurface surface in _surfaces.Values)
        {
            roots.Add(surface.Content);
        }

        return roots;
    }

    private void TrackActionObjects()
    {
        foreach (var entry in _actionObjects)
        {
            if (entry.Value is not null && (!_trackedActionObjects.TryGetValue(entry.Key, out var oldValue) || !ReferenceEquals(oldValue, entry.Value)))
            {
                _objectIdentity.Track(entry.Value);
            }

            _trackedActionObjects[entry.Key] = entry.Value;
        }
    }

    private void ReleaseDocument(int documentId)
    {
        var released = _objectIdentity.ReleaseDocument(documentId);
        _resourceScopes.RemoveObjects(released);
        _droppedChildren.Release(released);

        foreach (var key in new List<long>(_actionObjects.Keys))
        {
            if (_actionObjects[key] is object value && released.Contains(value))
            {
                _actionObjects.Remove(key);
                _trackedActionObjects.Remove(key);
            }
        }
    }

    private void Execute(
        XamlAction action,
        DeferredInsertionQueue<DeferredSetterInsertion> deferredSetterInsertions,
        Action<int, object?>? setSurfaceContentOverride = null)
    {
        switch (action)
        {
            case ConnectToLiveTreeAction connect:
                _actionObjects[connect.ProxyHandle] = ResolveObject(connect.LiveHandle);
                break;
            case SetSurfaceContentAction setSurfaceContent:
                object? surfaceContent = ResolveObject(setSurfaceContent.RootProxyHandle);
                if (setSurfaceContentOverride is not null)
                {
                    setSurfaceContentOverride(setSurfaceContent.DocumentId, surfaceContent);
                }
                else
                {
                    SetSurfaceContent(setSurfaceContent.DocumentId, surfaceContent);
                }

                break;
            // The stock TAP uses this live-object handle only to choose the dispatcher
            // for the following actions. This surface already executes the whole batch
            // on its single DispatcherQueue, so do not put the dispatcher into the
            // proxy/object map: the same handle still identifies the live WinUI object
            // used by a following ConnectToLiveTreeAction.
            case SetDispatcherAction:
                break;
            case CreateInstanceAction create when DroppedElementStandIn.IsTypeName(create.TypeName):
                // An element a namespace policy dropped from a collection: Visual Studio adds the stand-in where the
                // element is in its document model, and the collection records a hole there instead.
                _actionObjects[create.LiveHandle] = new DroppedElement();
                break;
            case CreateInstanceAction create:
                {
                    Type? runtimeType = XamlRuntimeUtilities.ResolveType(create.TypeName) ?? XamlRuntimeUtilities.ResolveType(create.FallbackTypeName);
                    if (runtimeType is null)
                    {
                        throw new TypeLoadException($"Unable to resolve '{create.TypeName}' (fallback '{create.FallbackTypeName}').");
                    }

                    object? instance;
                    if (typeof(Application).IsAssignableFrom(runtimeType))
                    {
                        // The process already owns its one WinUI Application. Resource actions
                        // target this instance without running the user's application startup.
                        instance = Application.Current;
                    }
                    else if (!string.IsNullOrWhiteSpace(create.FactoryMethod))
                    {
                        instance = XamlRuntimeUtilities.InvokeFactory(runtimeType, create.FactoryMethod, create.ArgumentTypes, create.ArgumentHandles, ResolveObject);
                    }
                    else if (create.ArgumentHandles is { Length: > 0 })
                    {
                        instance = XamlRuntimeUtilities.CreateInstance(runtimeType, create.ArgumentTypes, create.ArgumentHandles, ResolveObject);
                    }
                    else
                    {
                        instance = Activator.CreateInstance(runtimeType);
                    }

                    if (instance is null)
                    {
                        throw new InvalidOperationException($"Failed to create '{runtimeType.FullName}'.");
                    }

                    _actionObjects[create.LiveHandle] = instance;
                    _objectIdentity.GetHandle(instance);
                    if (create.MarkupHandle != 0)
                    {
                        _objectIdentity.RegisterMarkupInfo(instance, create.MarkupHandle, create.ChangeVersion);
                    }

                    break;
                }

            case CreateArrayAction createArray:
                _actionObjects[createArray.LiveHandle] = XamlRuntimeUtilities.CreateArray(
                    createArray.TypeName,
                    createArray.ItemHandles,
                    ResolveObject);
                break;
            case TypeConvertInstanceAction convert:
                _actionObjects[convert.LiveHandle] = XamlRuntimeUtilities.ConvertString(convert.MemberName, convert.Value, convert.IsEnum);
                break;
            case XamlParseInstanceAction parse:
                {
                    object instance = _resourceScopes.Parse(parse.OwnerHandle == 0 ? null : (ResolveObject(parse.OwnerHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {parse.OwnerHandle}.")), parse.Xaml);
                    _actionObjects[parse.LiveHandle] = instance;
                    _objectIdentity.GetHandle(instance);
                    if (parse.MarkupHandle != 0)
                    {
                        _objectIdentity.RegisterMarkupInfo(instance, parse.MarkupHandle, parse.ChangeVersion);
                    }

                    break;
                }

            case CreateImplicitDictionaryKeyAction implicitKey:
                _actionObjects[implicitKey.LiveHandle] = XamlRuntimeUtilities.CreateImplicitDictionaryKey(
                    implicitKey.Key,
                    implicitKey.KeyKind == ImplicitKeyKind.Type);
                break;
            case MeasureElementAction measure:
                XamlRuntimeUtilities.MeasureElement((ResolveObject(measure.ElementHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {measure.ElementHandle}.")));
                break;
            case GetPropertyAction getProperty:
                object valueOwner = ResolveObject(getProperty.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {getProperty.ParentHandle}.");
                if (valueOwner is DroppedElement)
                {
                    // Visual Studio builds nothing beneath a dropped element; the stand-in ends the path.
                    _actionObjects[getProperty.PropertyValueHandle] = valueOwner;
                    break;
                }

                object? propertyValue = XamlRuntimeUtilities.GetPropertyValue(valueOwner, getProperty.FullPropertyName);
                _actionObjects[getProperty.PropertyValueHandle] = propertyValue;
                _styleEdits.TrackPropertyValue(valueOwner, propertyValue);
                break;
            case SetPropertyAction setProperty:
                object propertyOwner = (ResolveObject(setProperty.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {setProperty.ParentHandle}."));
                if (propertyOwner is DroppedElement)
                {
                    // Nothing lives in place of a dropped element, so there is nothing to set.
                    break;
                }

                object? newValue = ResolveActionValue(setProperty.PropertyValueHandle, propertyOwner);
                if (newValue is DroppedElement)
                {
                    break;
                }

                if (!_styleEdits.TrySetProperty(propertyOwner, setProperty.FullPropertyName, newValue))
                {
                    XamlRuntimeUtilities.SetPropertyValue(propertyOwner, setProperty.FullPropertyName, newValue);
                }

                _styleEdits.TrackPropertySet(propertyOwner, setProperty.FullPropertyName);
                break;
            case ClearPropertyAction clearProperty:
                object clearedOwner = ResolveObject(clearProperty.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {clearProperty.ParentHandle}.");
                if (clearedOwner is DroppedElement)
                {
                    break;
                }

                if (!_styleEdits.TrySetProperty(clearedOwner, clearProperty.FullPropertyName, null))
                {
                    XamlRuntimeUtilities.ClearPropertyValue(clearedOwner, clearProperty.FullPropertyName, clearProperty.UseDefaultValue, clearProperty.DefaultValue, clearProperty.DefaultValueType);
                }

                break;
            case ClearCollectionPropertyAction clearCollection:
                object collectionOwner = ResolveObject(clearCollection.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {clearCollection.ParentHandle}.");
                if (collectionOwner is DroppedElement)
                {
                    break;
                }

                object? clearedCollection = XamlRuntimeUtilities.GetPropertyValue(collectionOwner, clearCollection.FullPropertyName);
                deferredSetterInsertions.RunAfterPendingInsertions(clearedCollection, InsertSetter, () =>
                {
                    // Visual Studio adds the children again afterwards, the dropped ones included.
                    _droppedChildren.ForgetCollection(collectionOwner, clearCollection.FullPropertyName);
                    if (!_styleEdits.TryClearSetters(collectionOwner, clearedCollection))
                    {
                        XamlRuntimeUtilities.ClearCollectionProperty(collectionOwner, clearCollection.FullPropertyName);
                    }
                });

                break;
            case UpdateEventHandlerAction updateEvent:
                XamlRuntimeUtilities.UpdateEventHandler((ResolveObject(updateEvent.EventOwner) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateEvent.EventOwner}.")), updateEvent.FullEventName, (ResolveObject(updateEvent.HandlerOwner) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateEvent.HandlerOwner}.")), updateEvent.HandlerOwnerType, updateEvent.OldHandler, updateEvent.NewHandler);
                break;
            case GetChildAction getChild:
                object lookupOwner = ResolveObject(getChild.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {getChild.ParentHandle}.");
                _actionObjects[getChild.ChildHandle] = _droppedChildren.GetChild(lookupOwner, getChild.Index, collection => InsertPendingSetters(deferredSetterInsertions, collection));
                break;
            case AddChildAction addChild:
                object childOwner = (ResolveObject(addChild.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {addChild.ParentHandle}."));

                // A stand-in takes no child, so its value is not resolved either. The setters that wait for the end of
                // the batch are part of the collection as Visual Studio sees it, and count for the index.
                object? child = childOwner is DroppedElement ? null : ResolveActionValue(addChild.ChildHandle, childOwner);
                if (_droppedChildren.PlaceChild(childOwner, addChild.Index, child, collection => deferredSetterInsertions.CountFor(collection)) is not int liveIndex)
                {
                    // Nothing is added: the child stands in for a dropped element and its position is a hole of the
                    // collection now, or the owner stands in for one.
                    break;
                }

                if (childOwner is SetterBaseCollection setterCollection && child is SetterBase setter)
                {
                    deferredSetterInsertions.Add(new(action, setterCollection, liveIndex, setter));
                }
                else
                {
                    XamlRuntimeUtilities.AddChild(childOwner, liveIndex, child);
                }

                break;
            case RemoveChildAction removeChild:
                object removalOwner = ResolveObject(removeChild.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {removeChild.ParentHandle}.");

                // The removal goes through the index translation; a sealed setter collection is changed through XAML
                // Diagnostics there (see RemoveChildAt).
                _droppedChildren.RemoveChild(removalOwner, removeChild.Index, collection => InsertPendingSetters(deferredSetterInsertions, collection));
                break;
            case GetDictionaryEntryAction getDictionaryEntry:
                _actionObjects[getDictionaryEntry.ValueHandle] = XamlRuntimeUtilities.GetDictionaryEntry((ResolveObject(getDictionaryEntry.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {getDictionaryEntry.DictionaryHandle}.")), ResolveObject(getDictionaryEntry.KeyHandle));
                break;
            case AddDictionaryEntryAction addDictionaryEntry:
                object dictionaryOwner = (ResolveObject(addDictionaryEntry.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {addDictionaryEntry.DictionaryHandle}."));
                object? addedKey = ResolveObject(addDictionaryEntry.KeyHandle);
                object? addedValue = ResolveActionValue(addDictionaryEntry.ValueHandle, dictionaryOwner);
                if (addedValue is not DroppedElement && !_styleEdits.TryAddDictionaryEntry(dictionaryOwner, addedKey, addedValue))
                {
                    XamlRuntimeUtilities.SetDictionaryEntry(dictionaryOwner, addedKey, addedValue);
                }

                break;
            case RemoveDictionaryEntryAction removeDictionaryEntry:
                object removalDictionary = (ResolveObject(removeDictionaryEntry.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {removeDictionaryEntry.DictionaryHandle}."));
                object? removedKey = ResolveObject(removeDictionaryEntry.KeyHandle);
                if (!_styleEdits.TryRemoveDictionaryEntry(removalDictionary, removedKey))
                {
                    XamlRuntimeUtilities.RemoveDictionaryEntry(removalDictionary, removedKey);
                }

                break;
            case UpdateDictionaryValueAction updateDictionaryValue:
                object updatedDictionaryOwner = (ResolveObject(updateDictionaryValue.DictionaryHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateDictionaryValue.DictionaryHandle}."));
                object? updatedKey = ResolveObject(updateDictionaryValue.KeyHandle);
                object? updatedValue = ResolveActionValue(updateDictionaryValue.ValueHandle, updatedDictionaryOwner);
                if (updatedValue is not DroppedElement && !_styleEdits.TryReplaceDictionaryEntry(updatedDictionaryOwner, updatedKey, updatedValue))
                {
                    XamlRuntimeUtilities.SetDictionaryEntry(updatedDictionaryOwner, updatedKey, updatedValue);
                }

                break;
            case NullReferenceAction nullReference:
                _actionObjects[nullReference.LiveHandle] = null;
                break;
            case MemberReferenceAction memberReference:
                _actionObjects[memberReference.LiveHandle] = XamlRuntimeUtilities.ResolveMember(memberReference.MemberName);
                break;
            case StaticReferenceAction staticReference:
                _actionObjects[staticReference.LiveHandle] = XamlRuntimeUtilities.ResolveMember(staticReference.MemberName);
                break;
            case FindElementAction findElement:
                _actionObjects[findElement.ElementHandle] = XamlRuntimeUtilities.FindElement((ResolveObject(findElement.LookupContextHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {findElement.LookupContextHandle}.")), findElement.ElementName) ?? throw new InvalidOperationException($"Unable to find element '{findElement.ElementName}' from handle {findElement.LookupContextHandle}.");
                break;
            case UpdateNameRegistrationAction updateName:
                XamlRuntimeUtilities.UpdateNameRegistration((ResolveObject(updateName.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {updateName.ParentHandle}.")), updateName.OldName, updateName.NewName);
                break;
            case InvokeMethodAction invoke:
                _actionObjects[invoke.LiveHandle] = XamlRuntimeUtilities.InvokeMethod(
                    invoke.InstanceHandle == 0 ? null : ResolveObject(invoke.InstanceHandle),
                    invoke.DeclaringType,
                    invoke.MethodName,
                    invoke.ParameterTypes,
                    invoke.ParameterHandles,
                    ResolveObject);
                break;
            case UpdateResourceMutationsAction resources:
                _resourceScopes.UpdateResources((ResolveObject(resources.Handle) ?? throw new KeyNotFoundException($"No live object is registered for handle {resources.Handle}.")));
                break;
            case SetResourcesSearchParentAction parent:
                _resourceScopes.SetParent((ResolveObject(parent.ChildHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {parent.ChildHandle}.")), parent.ParentHandle == 0 ? null : (ResolveObject(parent.ParentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {parent.ParentHandle}.")));
                break;
            case SetThemeResourcesEditingScopeAction theme:
                _resourceScopes.SetThemeScope(theme.Assembly, theme.RelativePath);
                break;
            case DisableXBindAction disableXBind:
                XamlRuntimeUtilities.DisableXBind((ResolveObject(disableXBind.DataTemplateComponentHandle) ?? throw new KeyNotFoundException($"No live object is registered for handle {disableXBind.DataTemplateComponentHandle}.")), disableXBind.LineNumber, disableXBind.ColumnNumber);
                break;
            case TapErrorAction tapError:
                throw new InvalidOperationException($"TAP action stream reported an error for handle {tapError.Handle}.");
            default:
                throw new NotSupportedException($"XAML action '{action.Action}' is not implemented by the WinUI designer surface.");
        }
    }

    // The indices of a batch assume that the earlier actions ran, so the setters that wait for the end of the batch go
    // into their collection before an index of it is used.
    private void InsertPendingSetters(DeferredInsertionQueue<DeferredSetterInsertion> pending, object collection)
    {
        pending.RunAfterPendingInsertions(collection, InsertSetter, static () => { });
    }

    /// <summary>
    /// Removes the child at a live index, after the index translation: a sealed setter collection through XAML
    /// Diagnostics, any other collection directly.
    /// </summary>
    /// <returns><see langword="false"/> when the index points past the end.</returns>
    private bool RemoveChildAt(object owner, int liveIndex)
    {
        if (owner is SetterBaseCollection setters && _styleEdits.TryRemoveSetter(setters, liveIndex))
        {
            return true;
        }

        return XamlRuntimeUtilities.RemoveChild(owner, liveIndex);
    }

    private object? ResolveObject(long handle)
    {
        if (_actionObjects.TryGetValue(handle, out var value))
        {
            if (value is not null)
            {
                _objectIdentity.Track(value);
            }

            return value;
        }

        if (_objectIdentity.TryGetObject(handle, out value))
        {
            if (value is not null)
            {
                _objectIdentity.Track(value);
            }

            return value;
        }

        return null;
    }

    private object? ResolveActionValue(long handle, object owner)
    {
        object? value = ResolveObject(handle);
        if (value is StaticResourceReference reference)
        {
            // The native UWP TAP replaces the reference's object state with its value.
            value = _resourceScopes.ResolveStaticResource(owner, reference.ResourceKey);
            _actionObjects[handle] = value;
        }

        return value;
    }
}
