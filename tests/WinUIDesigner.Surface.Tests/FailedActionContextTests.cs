// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Surface.Runtime;

[TestClass]
public sealed class FailedActionContextTests
{
    private static readonly string[] _Actions =
    [
        /* 0 */ """{"Action":1,"Id":1,"LiveHandle":-1,"TypeName":"Grid"}""",
        /* 1 */ """{"Action":1,"Id":2,"LiveHandle":-2,"TypeName":"Button"}""",
        /* 2 */ """{"Action":9,"Id":3,"ParentHandle":-1,"FullPropertyName":"Children","PropertyValueHandle":-3}""",
        /* 3 */ """{"Action":9,"Id":4,"ParentHandle":-2,"FullPropertyName":"KeyboardAccelerators","PropertyValueHandle":-4}""",
        /* 4 */ """{"Action":15,"Id":5,"ParentHandle":-3,"Index":0,"ChildHandle":-2}""",
        /* 5 */ """{"Action":15,"Id":6,"ParentHandle":-4,"Index":0,"ChildHandle":-5}""",
    ];

    [TestMethod]
    public void ActionsThatNameAHandleOfTheFailedActionAreFoundOldestFirst()
    {
        // Action 4 adds the button (-2) to the children (-3) of the grid: the button is created by action 1 and read by
        // action 3, the children are read by action 2.
        IReadOnlyList<(int Index, string Action)> related = FailedActionContext.FindRelated(_Actions, 4, 10);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, related.Select(entry => entry.Index).ToArray());
    }

    [TestMethod]
    public void OnlyTheLatestActionsAreKeptWhenThereAreMoreThanTheLimit()
    {
        IReadOnlyList<(int Index, string Action)> related = FailedActionContext.FindRelated(_Actions, 4, 2);

        // Actions 2 and 3 are the latest two of the three, and they stay in the order of the batch.
        CollectionAssert.AreEqual(new[] { 2, 3 }, related.Select(entry => entry.Index).ToArray());
    }

    [TestMethod]
    public void ActionsThatNameOtherHandlesAreLeftOut()
    {
        // Action 5 works on the accelerators (-4) of the button (-2): the grid and its children are not part of it.
        IReadOnlyList<(int Index, string Action)> related = FailedActionContext.FindRelated(_Actions, 5, 10);

        CollectionAssert.AreEqual(new[] { 3 }, related.Select(entry => entry.Index).ToArray());
    }

    [TestMethod]
    public void NumberOfAHandleMustMatchAsAWhole()
    {
        string[] actions =
        [
            """{"Action":1,"LiveHandle":-15}""",
            """{"Action":15,"ParentHandle":-1}""",
        ];

        // -1 is not -15.
        Assert.IsEmpty(FailedActionContext.FindRelated(actions, 1, 10));
    }

    [TestMethod]
    public void ZeroIsNoHandle()
    {
        string[] actions =
        [
            """{"Action":6,"LiveHandle":-1,"OwnerHandle":0}""",
            """{"Action":6,"LiveHandle":-2,"OwnerHandle":0}""",
        ];

        Assert.IsEmpty(FailedActionContext.FindRelated(actions, 1, 10));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(6)]
    public void IndexOutsideTheActionsFindsNothing(int failedIndex)
    {
        Assert.IsEmpty(FailedActionContext.FindRelated(_Actions, failedIndex, 10));
    }
}
