// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WinUIDesigner.Platform;

/// <summary>
/// Covers the rules of the Conditions button that need no designer: when it is shown, which column of the device
/// toolbar it takes, and what a key does to the list.
/// </summary>
[TestClass]
public sealed class ConditionalXamlSwitchButtonTests
{
    // The rows name the key action by the name of its member: a public test method cannot take the internal
    // enumeration itself, and nameof turns a renamed member into a compile error instead of a failing row.
    private const string _Open = nameof(ConditionalXamlSwitchButton.KeyAction.Open);
    private const string _Close = nameof(ConditionalXamlSwitchButton.KeyAction.Close);
    private const string _None = nameof(ConditionalXamlSwitchButton.KeyAction.None);

    [STATestMethod]
    public void TheButtonTakesTheColumnAfterTheLastOccupiedOneOfTheDeviceToolbar()
    {
        // The device toolbar of Visual Studio 18.13: eight columns; the device size, a separator, the orientation, a
        // separator, the effective resolution and a separator fill the first six, the last two are empty.
        Grid grid = CreateGrid(columns: 8, occupied: [0, 1, 2, 3, 4, 5]);

        Assert.AreEqual(6, ConditionalXamlSwitchButton.FindFreeColumn(grid));
    }

    [STATestMethod]
    public void AnEmptyToolbarGivesTheFirstColumn()
    {
        Assert.AreEqual(0, ConditionalXamlSwitchButton.FindFreeColumn(CreateGrid(columns: 8, occupied: [])));
    }

    [STATestMethod]
    public void AToolbarWithoutAFreeColumnGivesTheNumberOfColumns()
    {
        // The caller adds a column for the button.
        Grid grid = CreateGrid(columns: 8, occupied: [0, 3, 7]);

        Assert.AreEqual(grid.ColumnDefinitions.Count, ConditionalXamlSwitchButton.FindFreeColumn(grid));
    }

    [STATestMethod]
    public void AGapBeforeAnOccupiedColumnIsLeftAlone()
    {
        Assert.AreEqual(4, ConditionalXamlSwitchButton.FindFreeColumn(CreateGrid(columns: 8, occupied: [0, 3])));
    }

    [STATestMethod]
    public void AChildThatSpansColumnsOccupiesAllOfThem()
    {
        Grid grid = CreateGrid(columns: 8, occupied: [0]);
        var wide = new Border();
        Grid.SetColumn(wide, 1);
        Grid.SetColumnSpan(wide, 3);
        grid.Children.Add(wide);

        Assert.AreEqual(4, ConditionalXamlSwitchButton.FindFreeColumn(grid));
    }

    [TestMethod]
    [DataRow(true, 0, 0, false)]
    [DataRow(true, 2, 0, true)]
    [DataRow(true, 0, 3, true)]
    [DataRow(true, 2, 3, true)]
    [DataRow(false, 0, 0, false)]
    [DataRow(false, 2, 0, false)]
    [DataRow(false, 0, 3, false)]
    [DataRow(false, 2, 3, false)]
    public void TheButtonIsShownForConditionsOrOverridesWhenTheOptionIsOn(bool isEnabled, int switchCount, int overrideCount, bool expected)
    {
        Assert.AreEqual(expected, ConditionalXamlSwitchButton.IsShown(isEnabled, switchCount, overrideCount));
    }

    [TestMethod]
    [DataRow(Key.Down, false, _Open)]
    [DataRow(Key.F4, false, _Open)]
    [DataRow(Key.Down, true, _None)]
    [DataRow(Key.F4, true, _None)]
    [DataRow(Key.Escape, true, _Close)]
    [DataRow(Key.Escape, false, _None)]
    [DataRow(Key.Up, false, _None)]
    [DataRow(Key.Tab, true, _None)]
    [DataRow(Key.Space, false, _None)]
    public void KeysOpenAndCloseTheListFromTheButton(Key key, bool isOpen, string expected)
    {
        // Escape has to close an open list from the button as well: when no control of the list can take the focus,
        // the key reaches the button, and Visual Studio's toolbar would move the focus away and leave the list open.
        Assert.AreEqual(expected, ConditionalXamlSwitchButton.GetKeyAction(key, isOpen).ToString());
    }

    private static Grid CreateGrid(int columns, int[] occupied)
    {
        var grid = new Grid();
        for (int index = 0; index < columns; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        foreach (int column in occupied)
        {
            var child = new Border();
            Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }

        return grid;
    }
}
