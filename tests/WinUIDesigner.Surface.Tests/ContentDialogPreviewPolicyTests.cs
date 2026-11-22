// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using WinUIDesigner.Surface.Common;

[TestClass]
public sealed class ContentDialogPreviewPolicyTests
{
    // The limits that the template of WinUI gives the card (ContentDialogMinWidth, MaxWidth, MinHeight, MaxHeight).
    private static readonly CardLimits WinUITemplate = new(320, 548, 184, 756);

    [TestMethod]
    public void ADesignSizeAboveTheLimitsOfTheTemplateRaisesThem()
    {
        Assert.AreEqual(new CardLimits(320, 900, 184, 800), ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, 900, 800));
    }

    [TestMethod]
    public void ADesignSizeBelowTheLimitsOfTheTemplateLowersThem()
    {
        // Before: d:DesignWidth="280" and d:DesignHeight="150" gave a card of 320 by 184.
        Assert.AreEqual(new CardLimits(280, 548, 150, 756), ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, 280, 150));
    }

    [TestMethod]
    public void ADesignSizeWithinTheLimitsOfTheTemplateKeepsThem()
    {
        Assert.AreEqual(WinUITemplate, ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, 500, 300));
    }

    [TestMethod]
    public void WithoutADesignSizeTheCardHasTheLimitsOfTheTemplateAgain()
    {
        // Before: a MaxWidth raised to 900 for d:DesignWidth="900" stayed after the attribute was removed.
        CardLimits raised = ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, 900, 800);
        Assert.AreEqual(900.0, raised.MaxWidth);

        Assert.AreEqual(WinUITemplate, ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, double.NaN, double.NaN));
    }

    [TestMethod]
    public void EachDirectionHasItsOwnLimits()
    {
        Assert.AreEqual(new CardLimits(320, 900, 184, 756), ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, 900, double.NaN));
        Assert.AreEqual(new CardLimits(320, 548, 150, 756), ContentDialogPreviewPolicy.LimitsForDesignSize(WinUITemplate, double.NaN, 150));
    }

    [TestMethod]
    public void TheDialogIsTheArtboardOrAsLargeAsTheCardWithItsMargin()
    {
        // A card of 500 by 800 with a margin of 56 on each side, in an artboard of 1500 by 1000 and of 400 by 300.
        Assert.AreEqual((1500.0, 1000.0), ContentDialogPreviewPolicy.DialogArea(1500, 1000, 612, 912));
        Assert.AreEqual((612.0, 912.0), ContentDialogPreviewPolicy.DialogArea(400, 300, 612, 912));
    }
}
