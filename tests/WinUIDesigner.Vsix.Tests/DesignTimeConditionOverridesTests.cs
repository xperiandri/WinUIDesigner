// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.DesignTools.RuntimeHost.Networking;
using Microsoft.VisualStudio.DesignTools.RuntimeHost.TapOM;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.DesignTime;
using WinUIDesigner.Markup;
using WinUIDesigner.Platform;
using WinUIDesigner.Protocol;

/// <summary>
/// Covers the conditions the user forces as part of the design-time options: their field in the text form, the
/// override the user keeps per project, and the trip to the surface inside the CreateSurface request.
/// </summary>
[TestClass]
public sealed class DesignTimeConditionOverridesTests
{
    private const string _C7 = "IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)";
    private const string _Hostile = "using:My.App|FeatureFlagCondition( A;B=C,D%E \"quoted\" \\ é ∑ (x) )";

    private static readonly XamlConditionOverrides _Overrides = XamlConditionOverrides.Empty
        .With(_C7, holds: false)
        .With(_Hostile, holds: true);

    [TestMethod]
    public void OverridesAreAFieldOfTheTextForm()
    {
        var options = new DesignTimeOptions(DesignTimeTargetPlatform.Windows, null, XamlConditionOverrides.Empty.With(_C7, holds: false));

        Assert.AreEqual("v=1;target=win;cond=IsApiContractPresent(Windows.Foundation.UniversalApiContract%2C7)=0", options.Serialize());
        Assert.IsFalse(options.IsDefault);
        Assert.AreEqual("v=1;target=win", DesignTimeOptions.Default.Serialize(), "Without overrides the field is left out.");
    }

    [TestMethod]
    public void OptionsWithHostileOverridesSurviveARoundTrip()
    {
        foreach (DesignTimeTargetPlatform platform in DesignTimeTargetPlatform.All)
        {
            foreach (int? contractVersion in new int?[] { null, 7, 19 })
            {
                var options = new DesignTimeOptions(platform, contractVersion, _Overrides);

                Assert.IsTrue(DesignTimeOptions.TryParse(options.Serialize(), out DesignTimeOptions parsed), options.Serialize());
                Assert.AreEqual(options, parsed);
                Assert.AreEqual(_Overrides, parsed.ConditionOverrides);
                Assert.AreSame(platform, parsed.TargetPlatform);
                Assert.AreEqual(contractVersion, parsed.ContractVersion);
            }
        }
    }

    [TestMethod]
    public void OverridesTakePartInEquality()
    {
        var plain = new DesignTimeOptions(DesignTimeTargetPlatform.Ios, 10);
        var forced = new DesignTimeOptions(DesignTimeTargetPlatform.Ios, 10, _Overrides);
        var same = new DesignTimeOptions(DesignTimeTargetPlatform.Ios, 10, XamlConditionOverrides.Empty.With(_Hostile, true).With(_C7, false));

        Assert.AreNotEqual(plain, forced);
        Assert.AreEqual(forced, same);
        Assert.AreEqual(forced.GetHashCode(), same.GetHashCode());
        Assert.AreEqual(plain, forced.WithConditionOverrides(null));
        Assert.AreEqual(plain, new DesignTimeOptions(DesignTimeTargetPlatform.Ios, 10, XamlConditionOverrides.Empty));
    }

    [TestMethod]
    public void ChangingOneOptionKeepsTheOverrides()
    {
        DesignTimeOptions options = DesignTimeOptions.Default.WithConditionOverrides(_Overrides);

        Assert.AreEqual(_Overrides, options.WithTargetPlatform(DesignTimeTargetPlatform.Android).ConditionOverrides);
        Assert.AreEqual(_Overrides, options.WithContractVersion(7).ConditionOverrides);
        Assert.IsTrue(DesignTimeOptions.Default.ConditionOverrides.IsEmpty, "The default options must not change.");
    }

    [TestMethod]
    [DataRow("v=1;cond=a=1", "a", 1)]
    [DataRow("v=1;COND = a=1 , b=0 ;target=ios", "a,b", 2)]
    [DataRow("v=1;cond=a=1,broken,b=yes,c%ZZ=0", "a", 1)]
    [DataRow("v=1;cond=", "", 0)]
    [DataRow("v=1;cond", "", 0)]
    [DataRow("v=2;cond=a=0;more=fields", "a", 1)]
    public void OverridesAreReadLeniently(string text, string expectedKeys, int expectedCount)
    {
        Assert.IsTrue(DesignTimeOptions.TryParse(text, out DesignTimeOptions options));

        Assert.AreEqual(expectedCount, options.ConditionOverrides.Count);
        Assert.AreEqual(expectedKeys, string.Join(",", options.ConditionOverrides.Keys));
    }

    [TestMethod]
    public void OverridesExistOnlyInTheUserOverride()
    {
        string stored = DesignTimeOptionsResolver.CreateUserOverride(DesignTimeOptions.Default.WithConditionOverrides(_Overrides), null, null)!;

        // Nothing else differs from the project file, so the override holds the conditions alone.
        StringAssert.StartsWith(stored, "v=1;cond=");
        Assert.AreEqual(_Overrides, DesignTimeOptionsResolver.Resolve(stored, null, null).ConditionOverrides);
        Assert.AreEqual(_Overrides, DesignTimeOptionsResolver.Resolve(stored, "android", "1809").ConditionOverrides);
        Assert.AreEqual("android", DesignTimeOptionsResolver.Resolve(stored, "android", "1809").TargetPlatformId, "The project file still decides what the user did not change.");

        Assert.IsTrue(DesignTimeOptionsResolver.ResolveDefaults("android", "1809").ConditionOverrides.IsEmpty);
        Assert.IsNull(DesignTimeOptionsResolver.CreateUserOverride(DesignTimeOptions.Default, null, null), "Without overrides there is nothing to keep.");
    }

    [TestMethod]
    public void StoredOverrideWithConditionsResolvesBackToTheChosenOptions()
    {
        foreach (string? targetPlatformProperty in new[] { null, "android", "bogus" })
        {
            foreach (string? windowsVersionProperty in new[] { null, "1809", "bogus" })
            {
                foreach (DesignTimeTargetPlatform platform in new[] { DesignTimeTargetPlatform.Windows, DesignTimeTargetPlatform.Android, DesignTimeTargetPlatform.WebAssemblySkia })
                {
                    foreach (int? contractVersion in new int?[] { null, 7, 19 })
                    {
                        var options = new DesignTimeOptions(platform, contractVersion, _Overrides);
                        string? stored = DesignTimeOptionsResolver.CreateUserOverride(options, targetPlatformProperty, windowsVersionProperty);

                        Assert.IsNotNull(stored);
                        Assert.AreEqual(
                            options,
                            DesignTimeOptionsResolver.Resolve(stored, targetPlatformProperty, windowsVersionProperty),
                            $"'{stored}' with {targetPlatformProperty}/{windowsVersionProperty}");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void CreateSurfaceRequestCarriesTheOverrides()
    {
        // The request of the extension, written by Visual Studio's own Message the way the protocol handler writes it
        // and read back by it, hostile text of the conditions included.
        string options = new DesignTimeOptions(DesignTimeTargetPlatform.Windows, null, _Overrides).Serialize();
        var request = new WinUICreateSurfaceRequestInfo
        {
            Document = new CreateDocumentInfo { DocumentId = 42 },
            SerializedDesignTimeOptions = options,
        };

        byte[] buffer = new Message((int)DesignerMessageId.CreateSurface, request).BufferForSend;
        WinUICreateSurfaceRequestInfo? received = new Message(buffer).ConvertPayloadToModel<WinUICreateSurfaceRequestInfo>();

        Assert.IsNotNull(received);
        Assert.AreEqual(options, received.SerializedDesignTimeOptions);
        Assert.IsTrue(DesignTimeOptions.TryParse(received.SerializedDesignTimeOptions, out DesignTimeOptions parsed));
        Assert.AreEqual(_Overrides, parsed.ConditionOverrides);
    }
}
