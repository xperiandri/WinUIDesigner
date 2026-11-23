// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Text;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Protocol;
using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class CreateSurfaceRequestContractTests
{
    [TestMethod]
    public void TheSurfaceReadsTheOptionsOfTheExtensionsRequest()
    {
        // The payload Visual Studio's Message writes for the request of the extension: Visual Studio's members, then
        // the options under the member name the extension's tests show it writes. Read on the runtime of the surface.
        string payload =
            $"{{\"Application\":null,\"DesignTimeResources\":null,\"Document\":{{\"DocumentId\":42}},\"{DesignTimeOptions.WireMemberName}\":\"v=1;target=android;contract=7\"}}";

        CreateSurfaceRequestContract? request = Read(payload);

        Assert.IsNotNull(request);
        Assert.AreEqual(42, request.Document?.DocumentId);
        Assert.AreEqual("v=1;target=android;contract=7", request.SerializedDesignTimeOptions);
        Assert.IsTrue(DesignTimeOptions.TryParse(request.SerializedDesignTimeOptions, out DesignTimeOptions options));
        Assert.AreEqual(new DesignTimeOptions(DesignTimeTargetPlatform.Android, 7), options);
    }

    [TestMethod]
    public void VisualStudiosOwnRequestReadsAsNoOptions()
    {
        // What the extension sends while the options are turned off: Visual Studio's request, written by its Message.
        var sent = new Message(
            (int)DesignerMessageId.CreateSurface,
            new CreateSurfaceRequestInfo { Document = new CreateDocumentInfo { DocumentId = 42 } }
        );

        CreateSurfaceRequestContract? request = new Message(sent.BufferForSend).ConvertPayloadToModel<CreateSurfaceRequestContract>();

        Assert.IsNotNull(request);
        Assert.AreEqual(42, request.Document?.DocumentId);
        Assert.IsNull(request.SerializedDesignTimeOptions);
        Assert.IsFalse(DesignTimeOptions.TryParse(request.SerializedDesignTimeOptions, out DesignTimeOptions options));
        Assert.AreSame(DesignTimeOptions.Default, options);
    }

    // A message framed as Visual Studio frames it: the header, then the payload as UTF-16 JSON.
    private static CreateSurfaceRequestContract? Read(string payload)
    {
        byte[] json = Encoding.Unicode.GetBytes(payload);
        byte[] buffer = new byte[Message.PayloadOffset + json.Length];
        json.CopyTo(buffer, Message.PayloadOffset);
        return new Message(buffer).ConvertPayloadToModel<CreateSurfaceRequestContract>();
    }
}
