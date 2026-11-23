// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using Microsoft.VisualStudio.DesignTools.DesignerContract;
using Microsoft.VisualStudio.DesignTools.Markup;
using Microsoft.VisualStudio.DesignTools.Markup.DocumentModel;
using Microsoft.VisualStudio.DesignTools.Markup.Metadata;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.Project;
using Microsoft.VisualStudio.DesignTools.SurfaceDesigner.Documents.SurfaceIsolation;
using Microsoft.VisualStudio.DesignTools.Utility;
using Microsoft.VisualStudio.DesignTools.UwpSurfaceDesigner.Documents;

namespace WinUIDesigner.Platform;

internal sealed class WinUISurfaceProcessMarkupProvider : UwpSurfaceProcessMarkupProvider
{
    public ISurfaceProcessDocument? DesignTimeResources { get; private set; }

    public override void PrepareApplicationDocumentsForLoading(bool ignoreAppXbf)
    {
        base.PrepareApplicationDocumentsForLoading(ignoreAppXbf: true);
        DesignTimeResources = Project.DesignTimeResources is { } resources
            ? PrepareDocumentForLoading(resources)
            : null;
    }

    /// <summary>
    /// Prepares the dictionary a document merges through <c>ms-appx:///Assembly/Path.xaml</c> also when it is in a
    /// project that the application references only through another project.
    /// </summary>
    /// <remarks>
    /// Visual Studio looks for the assembly named in such a source among the projects that the application references
    /// directly (<see cref="Microsoft.VisualStudio.DesignTools.Xaml.LanguageService.UwpUriResolver.FindTargetProject"/>), also for the document of a library. A library that a
    /// library of the application references is not among them, so the source is not found and keeps the value as
    /// written (<see cref="ResolveItemDetails.ReferencedAssembly"/>), and the surface cannot merge a dictionary that
    /// Visual Studio did not prepare. When Visual Studio finds nothing for that reason, this looks for the project
    /// among the references of the document's own project first, then among everything that the application reaches,
    /// and prepares the dictionary the way Visual Studio prepares a related document it did find. The answer of
    /// Visual Studio stands for everything else.
    /// </remarks>
    public override SurfaceProcessDocumentResult RegisterRelatedDocument(string documentRelativePath, IDocumentContext ownerDocument, bool prepareForLoading)
    {
        // Visual Studio looks first, and its answer stands unless it found no project for the assembly of the source.
        SurfaceProcessDocumentResult result = base.RegisterRelatedDocument(documentRelativePath, ownerDocument, prepareForLoading);
        if (result.Document is not null || result.MissingReason != ResolveItemDetails.ReferencedAssembly)
        {
            return result;
        }

        try
        {
            return TryRegisterLibraryDocument(documentRelativePath, ownerDocument, prepareForLoading) ?? result;
        }
        catch (Exception exception)
        {
            WinUIDesignerLogger.LogWarning("Host", $"Looking for the resource dictionary '{documentRelativePath}' in the projects that the application references through other projects failed; Visual Studio's answer stands.", exception);
            return result;
        }
    }

    private SurfaceProcessDocumentResult? TryRegisterLibraryDocument(string source, IDocumentContext ownerDocument, bool prepareForLoading)
    {
        // The source has to name a file of a project by the assembly name of that project.
        if (!AppxLibraryUri.TrySplit(source, out string assemblyName, out string projectRelativePath))
        {
            return null;
        }

        // The project of the document that names it, and the application that project belongs to.
        if (ownerDocument.Project is not ProjectContextBase ownerContext || ownerContext.HostProject is not IHostProject owner)
        {
            return null;
        }

        IHostProject? application = owner.IsExecutable ? owner : owner.FindApplicationProject();

        // The project that has the assembly name, and the file in it.
        ProjectLookupResult<IHostProject>? found = TransitiveProjectLookup.Find<IHostProject>(
            owner,
            application,
            project => project.ReferencedProjects,
            project => project.TargetAssemblyName,
            project => project.ProjectGuid,
            assemblyName
        );
        IHostSourceItem? item = found?.Project.FindItemByProjectRelativePath(projectRelativePath);
        if (found is null || item is null)
        {
            return null;
        }

        // The document is opened the way Visual Studio opens the documents of every project: through the document that names it.
        if (ownerDocument.GetDocumentRoot(item) is not IMarkupDocument document)
        {
            return null;
        }

        // A copy is prepared once, and Visual Studio does not prepare it again when it is edited: a copy that the cleaner
        // makes before the project knows its own types lacks every style whose target type it cannot resolve.
        IProjectContext? library = (Project as ProjectContextBase)?.DesignerContext.ProjectContextManager.GetProjectContext(found.Project.ProjectGuid, create: false);
        if (!CanResolveItsOwnTypes(library))
        {
            WinUIDesignerLogger.LogInformation("Host", $"The resource dictionary '{source}' is in the project '{found.Project.ProjectName}', which does not know its own types yet; it is not prepared.");
            return null;
        }

        SurfaceProcessDocument registered = RegisterDocumentInternal(document, prepareForLoading);
        WinUIDesignerLogger.LogInformation("Host", $"The resource dictionary '{source}' is in the project '{found.Project.ProjectName}', which {found.DescribeRoute(project => project.ProjectName)}; it is prepared.");
        return new SurfaceProcessDocumentResult(registered, ResolveItemDetails.None);
    }

    private static bool CanResolveItsOwnTypes(IProjectContext? project)
    {
        // Its metadata is ready (the compilation of the project exists) and has the assembly of the project in it.
        // IAssembly.IsLoaded is not asked: the managed assembly of Visual Studio 18.13 answers true whatever its state.
        return project?.Metadata is { IsReady: true, ProjectAssembly: not null };
    }
}
